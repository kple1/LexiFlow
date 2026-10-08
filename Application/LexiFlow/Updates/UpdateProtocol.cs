using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LexiFlow.Updates;

public sealed record UpdateEnvelope
{
    [JsonRequired] public string Payload { get; init; } = "";
    [JsonRequired] public string Signature { get; init; } = "";
}
public sealed record UpdateManifest
{
    [JsonRequired] public int Schema { get; init; }
    [JsonRequired] public string Product { get; init; } = "";
    [JsonRequired] public string Platform { get; init; } = "";
    [JsonRequired] public string Version { get; init; } = "";
    [JsonRequired] public string Url { get; init; } = "";
    [JsonRequired] public long Length { get; init; }
    [JsonRequired] public string Sha256 { get; init; } = "";
    [JsonRequired] public DateTimeOffset PublishedAt { get; init; }
    [JsonRequired] public DateTimeOffset ExpiresAt { get; init; }
}
public sealed record VerifiedUpdate(UpdateManifest Manifest, Uri DownloadUri, Version Version, byte[] Envelope);

public static class UpdateProtocol
{
    public const string Origin = "https://lexiflow.duckdns.org";
    public const string FeedUrl = Origin + "/updates/windows-x64/latest.json";
    public const long MaxDownloadBytes = 512L * 1024 * 1024;
    public static readonly JsonSerializerOptions JsonOptions = new()
    { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 5 };

    public static T ReadStrict<T>(ReadOnlySpan<byte> bytes)
    {
        using var doc = JsonDocument.Parse(bytes.ToArray(), new() { MaxDepth = 5 });
        NoDuplicates(doc.RootElement);
        return JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new InvalidDataException("Empty update document.");
    }
    private static void NoDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in value.EnumerateObject())
            {
                if (!names.Add(item.Name)) throw new InvalidDataException("Duplicate update field.");
                NoDuplicates(item.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) NoDuplicates(item);
    }
    public static Version ParseVersion(string value)
    {
        if (!Version.TryParse(value, out var version) || version.Revision != -1 || version.Build < 0 || version.ToString(3) != value)
            throw new InvalidDataException("Invalid release version.");
        return version;
    }
    public static VerifiedUpdate Verify(byte[] bytes, string publicKey, Version current, DateTimeOffset now, bool requireNewer = true)
    {
        if (bytes.Length is 0 or > 16384) throw new InvalidDataException("Oversized update document.");
        var envelope = ReadStrict<UpdateEnvelope>(bytes);
        var payload = Convert.FromBase64String(envelope.Payload);
        var signature = Convert.FromBase64String(envelope.Signature);
        if (payload.Length is 0 or > 8192 || signature.Length != 64) throw new InvalidDataException("Invalid release signature.");
        using var key = ECDsa.Create();
        var spki = Convert.FromBase64String(publicKey);
        key.ImportSubjectPublicKeyInfo(spki, out var consumed);
        if (consumed != spki.Length || key.KeySize != 256 || !key.VerifyData(payload, signature, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) throw new CryptographicException("Untrusted update.");
        var manifest = ReadStrict<UpdateManifest>(payload);
        var version = ParseVersion(manifest.Version);
        if (manifest.Schema != 1 || manifest.Product != "LexiFlow" || manifest.Platform != "windows-x64"
            || manifest.Length is < 2 or > MaxDownloadBytes || manifest.Sha256 is not { Length: 64 } || !manifest.Sha256.All(char.IsAsciiHexDigit)
            || manifest.PublishedAt.Offset != TimeSpan.Zero || manifest.ExpiresAt.Offset != TimeSpan.Zero
            || manifest.PublishedAt > now.AddMinutes(5) || manifest.ExpiresAt <= now || manifest.ExpiresAt <= manifest.PublishedAt
            || manifest.ExpiresAt > manifest.PublishedAt.AddDays(366)) throw new InvalidDataException("Invalid release policy.");
        var expected = Origin + "/updates/windows-x64/" + manifest.Version + "/LexiFlow.exe";
        if (!Uri.TryCreate(manifest.Url, UriKind.Absolute, out var uri) || manifest.Url != expected)
            throw new InvalidDataException("Untrusted download location.");
        if (requireNewer && version <= current) throw new InvalidDataException("Update is not newer.");
        return new(manifest, uri, version, bytes.ToArray());
    }
    public static HttpClient CreateHttpClient() => new(new HttpClientHandler
    { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None }) { Timeout = Timeout.InfiniteTimeSpan };
    public static async Task<byte[]> ReadBoundedAsync(Stream input, int max, CancellationToken token)
    {
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + count > max) throw new InvalidDataException("Oversized update document.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    public static async Task DownloadAsync(HttpClient client, VerifiedUpdate update, string destination, IProgress<double>? progress, CancellationToken token)
    {
        using var response = await client.GetAsync(update.DownloadUri, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentEncoding.Count != 0
            || (response.Content.Headers.ContentLength is long length && length != update.Manifest.Length))
            throw new InvalidDataException("Invalid update download response.");
        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long received = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, token)) > 0)
        {
            received += count;
            if (received > update.Manifest.Length) throw new InvalidDataException("Oversized download.");
            hash.AppendData(buffer, 0, count);
            await target.WriteAsync(buffer.AsMemory(0, count), token);
            progress?.Report((double)received / update.Manifest.Length);
        }
        if (received != update.Manifest.Length || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(update.Manifest.Sha256)))
            throw new CryptographicException("Downloaded file verification failed.");
        await target.FlushAsync(token); target.Flush(true);
    }
}
