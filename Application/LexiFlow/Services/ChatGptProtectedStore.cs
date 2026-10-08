using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LexiFlow.Services;

internal interface IChatGptSecureStore
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    void Remove(string key);
}

internal sealed class ChatGptSecureStore : IChatGptSecureStore
{
    public Task<string?> GetAsync(string key) => Microsoft.Maui.Storage.SecureStorage.Default.GetAsync(key);
    public Task SetAsync(string key, string value) => Microsoft.Maui.Storage.SecureStorage.Default.SetAsync(key, value);
    public void Remove(string key) => Microsoft.Maui.Storage.SecureStorage.Default.Remove(key);
}

// A single SecureStorage item can exceed Windows packaged LocalSettings' 8 KiB limit.
// Stage bounded chunks first, then atomically replace the small protected manifest.
internal sealed class ChatGptProtectedStore(IChatGptSecureStore store)
{
    internal const int ChunkSize = 1500;
    private const int MaxChunks = 512;
    private sealed class Manifest
    {
        public string Generation { get; set; } = "";
        public int Count { get; set; }
        public string Hash { get; set; } = "";
    }
    private static string Prefix(string key) => "chatgpt_v1_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    private static bool Valid(Manifest? value) => value is { Count: > 0 and <= MaxChunks }
        && Guid.TryParseExact(value.Generation, "N", out _) && value.Hash.Length == 64;

    public async Task<T?> ReadAsync<T>(string key) where T : class
    {
        try
        {
            var prefix = Prefix(key);
            var raw = await store.GetAsync(prefix);
            if (raw is null) return null;
            if (raw.Length > 1024) throw new InvalidDataException();
            var manifest = JsonSerializer.Deserialize<Manifest>(raw);
            if (!Valid(manifest)) throw new InvalidDataException();
            var text = new StringBuilder();
            for (var i = 0; i < manifest!.Count; i++)
            {
                var chunk = await store.GetAsync($"{prefix}_{manifest.Generation}_{i}");
                if (chunk is null || chunk.Length > ChunkSize) throw new InvalidDataException();
                text.Append(chunk);
            }
            var json = text.ToString();
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))), manifest.Hash, StringComparison.Ordinal))
                throw new InvalidDataException();
            return JsonSerializer.Deserialize<T>(json) ?? throw new InvalidDataException();
        }
        catch (ChatGptException) { throw; }
        catch { throw new ChatGptException(ChatGptFailureKind.StorageUnavailable); }
    }

    public async Task WriteAsync<T>(string key, T value)
    {
        var prefix = Prefix(key);
        var generation = Guid.NewGuid().ToString("N");
        var written = 0;
        try
        {
            var oldRaw = await store.GetAsync(prefix);
            Manifest? old = null;
            if (oldRaw is not null)
            {
                if (oldRaw.Length > 1024) throw new InvalidDataException();
                old = JsonSerializer.Deserialize<Manifest>(oldRaw);
                if (!Valid(old)) throw new InvalidDataException();
            }
            var json = JsonSerializer.Serialize(value);
            var count = (json.Length + ChunkSize - 1) / ChunkSize;
            if (count is < 1 or > MaxChunks) throw new InvalidDataException();
            for (var i = 0; i < count; i++)
            {
                await store.SetAsync($"{prefix}_{generation}_{i}", json.Substring(i * ChunkSize, Math.Min(ChunkSize, json.Length - i * ChunkSize)));
                written++;
            }
            await store.SetAsync(prefix, JsonSerializer.Serialize(new Manifest
            {
                Generation = generation, Count = count,
                Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            }));
            if (old is not null) Cleanup(prefix, old.Generation, old.Count);
        }
        catch
        {
            // If SetAsync completed but then failed, never remove a committed generation.
            try
            {
                var current = await store.GetAsync(prefix);
                if (current is null || !current.Contains(generation, StringComparison.Ordinal)) Cleanup(prefix, generation, written);
            }
            catch { }
            throw new ChatGptException(ChatGptFailureKind.StorageUnavailable);
        }
    }

    private void Cleanup(string prefix, string generation, int count)
    {
        for (var i = 0; i < count; i++)
        {
            try { store.Remove($"{prefix}_{generation}_{i}"); }
            catch { /* Only unreachable, protected chunks remain; never fall back to them. */ }
        }
    }
}

internal static class ChatGptProcessLock
{
    // A named mutex serializes rotating refresh tokens across app processes.
    // Its owner thread stays alive until the asynchronous operation completes.
    // Unlike a named semaphore, it is recoverable after a process crashes.
    public static Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken ct) => Task.Run(() =>
    {
        using var mutex = new Mutex(false, @"Local\LexiFlow.ChatGpt.Credentials.v1");
        var acquired = false;
        try
        {
            var started = DateTimeOffset.UtcNow;
            while (!acquired)
            {
                ct.ThrowIfCancellationRequested();
                if (DateTimeOffset.UtcNow - started > TimeSpan.FromSeconds(45))
                    throw new ChatGptException(ChatGptFailureKind.Unavailable);
                try { acquired = mutex.WaitOne(100); }
                catch (AbandonedMutexException) { acquired = true; }
            }
            ct.ThrowIfCancellationRequested();
            return action().GetAwaiter().GetResult();
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }, ct);
}

internal sealed class ChatGptHostRecord
{
    public string HostId { get; set; } = "";
}

internal sealed class ChatGptOwnerRecord
{
    public string OwnerKey { get; set; } = "";
    public string? ActiveProfileId { get; set; }
    public string? PendingProfileId { get; set; }
    public List<ChatGptProfileRecord> Profiles { get; set; } = [];
}

internal sealed class ChatGptProfileRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ClientId { get; set; } = "";
    public string? Subject { get; set; }
    public string? Email { get; set; }
    public ChatGptCredentials? Credentials { get; set; }
}

internal sealed class ChatGptCredentials
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string IdToken { get; set; } = "";
    public string Scope { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset SavedAt { get; set; }
    public DateTimeOffset? EarliestRefreshAt { get; set; }
    public override string ToString() => "ChatGptCredentials [redacted]";
}
