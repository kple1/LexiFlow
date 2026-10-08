using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LexiFlow.Services;

/// <summary>
/// Stores large strings using small Preferences values. A new manifest is published
/// only after every chunk is written; readers never combine different generations.
/// Legacy single-value preferences remain readable until the first successful write.
/// </summary>
public static class LargePreferenceStore
{
    public const int ChunkSize = 1500;
    private const int MaximumChunks = 20_000;
    private static readonly object Gate = new();

    public static string ManifestKey(string key) => key + ":large-v1";
    private static string ChunkKey(string key, string generation, int index)
        => $"{ManifestKey(key)}:{generation}:{index}";

    public static bool Contains(string key)
    {
        lock (Gate) return Preferences.ContainsKey(ManifestKey(key)) || Preferences.ContainsKey(key);
    }

    public static string Get(string key, string fallback)
    {
        lock (Gate)
        {
            var manifest = ReadManifest(key);
            if (manifest is null) return Preferences.Get(key, fallback);
            var text = new StringBuilder(manifest.Length);
            for (var index = 0; index < manifest.Count; index++)
            {
                var partKey = ChunkKey(key, manifest.Generation, index);
                if (!Preferences.ContainsKey(partKey)) throw Corrupt();
                var part = Preferences.Get(partKey, "");
                if (part.Length == 0 || part.Length > ChunkSize) throw Corrupt();
                text.Append(part);
                if (text.Length > manifest.Length) throw Corrupt();
            }
            var value = text.ToString();
            if (value.Length != manifest.Length || Hash(value) != manifest.Sha256) throw Corrupt();
            return value;
        }
    }

    public static void Set(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > (ChunkSize - 1) * MaximumChunks)
            throw new ArgumentException("The preference value is too large.", nameof(value));
        lock (Gate)
        {
            var previous = ReadManifest(key);
            var generation = Guid.NewGuid().ToString("N");
            var written = 0;
            try
            {
                for (var offset = 0; offset < value.Length;)
                {
                    var length = Math.Min(ChunkSize, value.Length - offset);
                    if (offset + length < value.Length && char.IsHighSurrogate(value[offset + length - 1]) &&
                        char.IsLowSurrogate(value[offset + length])) length--;
                    Preferences.Set(ChunkKey(key, generation, written), value.Substring(offset, length));
                    written++;
                    offset += length;
                }
                var manifest = new Manifest(1, generation, written, value.Length, Hash(value));
                Preferences.Set(ManifestKey(key), JsonSerializer.Serialize(manifest));
            }
            catch
            {
                // Some storage providers can throw after completing a write. Never delete
                // chunks if that generation became the committed value despite the error.
                try
                {
                    if (ReadManifest(key)?.Generation != generation)
                        RemoveChunksBestEffort(key, generation, written + 1);
                }
                catch { /* Retain uncertain chunks instead of risking the committed value. */ }
                throw;
            }

            // A committed value stays successful even if old-value cleanup is unavailable.
            if (previous is not null) RemoveChunksBestEffort(key, previous.Generation, previous.Count);
            try { Preferences.Remove(key); } catch { }
        }
    }

    public static void Remove(string key)
    {
        lock (Gate)
        {
            var manifest = ReadManifest(key);
            if (manifest is not null)
            {
                // Keep the manifest until all referenced chunks are removed, so a failed
                // cleanup can be retried instead of losing the only list of remaining keys.
                for (var index = 0; index < manifest.Count; index++)
                    Preferences.Remove(ChunkKey(key, manifest.Generation, index));
            }
            Preferences.Remove(key);
            Preferences.Remove(ManifestKey(key));
        }
    }

    private static Manifest? ReadManifest(string key)
    {
        if (!Preferences.ContainsKey(ManifestKey(key))) return null;
        Manifest manifest;
        try { manifest = JsonSerializer.Deserialize<Manifest>(Preferences.Get(ManifestKey(key), "")) ?? throw Corrupt(); }
        catch (JsonException) { throw Corrupt(); }
        if (manifest.Version != 1 || manifest.Generation is not { Length: 32 } ||
            !manifest.Generation.All(char.IsAsciiHexDigit) || manifest.Count is < 0 or > MaximumChunks ||
            manifest.Length < 0 || manifest.Length > manifest.Count * ChunkSize ||
            (manifest.Count == 0) != (manifest.Length == 0) ||
            manifest.Sha256 is not { Length: 64 } || !manifest.Sha256.All(char.IsAsciiHexDigit))
            throw Corrupt();
        return manifest;
    }

    private static void RemoveChunksBestEffort(string key, string generation, int count)
    {
        for (var index = 0; index < count; index++)
            try { Preferences.Remove(ChunkKey(key, generation, index)); } catch { }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static InvalidDataException Corrupt() => new("Stored learning data is incomplete or damaged; it was not replaced.");
    private sealed record Manifest(int Version, string Generation, int Count, int Length, string Sha256);
}
