using LexiFlow.Updates;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }
void Denied(Action action, string name) { try { action(); } catch (Exception e) when (e is InvalidDataException or CryptographicException or JsonException or FormatException or IOException) { Check(true, name); return; } throw new Exception("Accepted " + name); }
async Task DeniedAsync(Func<Task> action, string name) { try { await action(); } catch (Exception e) when (e is InvalidDataException or CryptographicException or HttpRequestException or OperationCanceledException or IOException) { Check(true, name); return; } throw new Exception("Accepted " + name); }
using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
var now = DateTimeOffset.UtcNow;
var body = Encoding.UTF8.GetBytes("MZ signed executable placeholder for isolated tests");
var manifest = new UpdateManifest { Schema = 1, Product = "LexiFlow", Platform = "windows-x64", Version = "1.6.1", Url = UpdateProtocol.Origin + "/updates/windows-x64/1.6.1/LexiFlow.exe", Length = body.Length, Sha256 = Convert.ToHexString(SHA256.HashData(body)), PublishedAt = now, ExpiresAt = now.AddDays(180) };
byte[] Sign(UpdateManifest value) => SignRaw(JsonSerializer.SerializeToUtf8Bytes(value));
byte[] SignRaw(byte[] value) => JsonSerializer.SerializeToUtf8Bytes(new UpdateEnvelope { Payload = Convert.ToBase64String(value), Signature = Convert.ToBase64String(key.SignData(value, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) });
VerifiedUpdate Verify(byte[] value) => UpdateProtocol.Verify(value, publicKey, new(1, 6, 0), now);
var signed = Sign(manifest);
var verified = Verify(signed);
Check(verified.Version == new Version(1, 6, 1) && verified.DownloadUri.AbsoluteUri == manifest.Url, "valid pinned-key release accepted");
Denied(() => UpdateProtocol.Verify(signed, Convert.ToBase64String(other.ExportSubjectPublicKeyInfo()), new(1, 6, 0), now), "wrong signing key rejected");
var envelope = JsonSerializer.Deserialize<UpdateEnvelope>(signed)!;
var changed = Convert.FromBase64String(envelope.Payload); changed[3] ^= 1;
Denied(() => Verify(JsonSerializer.SerializeToUtf8Bytes(envelope with { Payload = Convert.ToBase64String(changed) })), "modified payload rejected");
var sig = Convert.FromBase64String(envelope.Signature); sig[0] ^= 1;
Denied(() => Verify(JsonSerializer.SerializeToUtf8Bytes(envelope with { Signature = Convert.ToBase64String(sig) })), "modified signature rejected");
Denied(() => Verify(Encoding.UTF8.GetBytes("{}")), "missing envelope fields rejected");
Denied(() => Verify(new byte[16385]), "oversized envelope rejected");
Denied(() => Verify(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(signed).TrimEnd('}') + ",\"Payload\":\"\"}")), "duplicate envelope fields rejected");
Denied(() => Verify(SignRaw(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest).TrimEnd('}') + ",\"Schema\":1}"))), "signed duplicate payload fields rejected");
Denied(() => Verify(SignRaw(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest).TrimEnd('}') + ",\"Unknown\":1}"))), "signed unknown fields rejected");
foreach (var invalid in new[] {
    manifest with { Product = "Other" }, manifest with { Platform = "linux-x64" }, manifest with { Schema = 2 },
    manifest with { Version = "1.6.0", Url = UpdateProtocol.Origin + "/updates/windows-x64/1.6.0/LexiFlow.exe" },
    manifest with { Version = "1.5.9", Url = UpdateProtocol.Origin + "/updates/windows-x64/1.5.9/LexiFlow.exe" },
    manifest with { Version = "01.6.1" }, manifest with { Version = "1.6.1.0" }, manifest with { Version = "1.6" },
    manifest with { Length = 0 }, manifest with { Length = UpdateProtocol.MaxDownloadBytes + 1 },
    manifest with { Sha256 = "bad" }, manifest with { ExpiresAt = now }, manifest with { ExpiresAt = now.AddDays(400) },
    manifest with { PublishedAt = now.AddHours(1) }, manifest with { PublishedAt = now.ToOffset(TimeSpan.FromHours(9)) },
    manifest with { Url = manifest.Url.Replace("https", "http") }, manifest with { Url = "https://example.test/LexiFlow.exe" },
    manifest with { Url = manifest.Url + "?token=secret" }, manifest with { Url = manifest.Url + "#file" },
    manifest with { Url = UpdateProtocol.Origin + "/updates/windows-x64/1.6.1/../LexiFlow.exe" },
    manifest with { Url = "https://user@lexiflow.duckdns.org/updates/windows-x64/1.6.1/LexiFlow.exe" }
}) Denied(() => Verify(Sign(invalid)), "release policy rejects " + invalid.Version + "/" + checks);
Check(UpdateProtocol.Verify(signed, publicKey, new(1, 6, 1), now, false).Version == new Version(1, 6, 1), "equal current version can be checked without reinstalling");
var directory = Path.Combine(Path.GetTempPath(), "lexiflow-update-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var handler = new FakeDownload { Body = body };
    using var client = new HttpClient(handler);
    var source = Path.Combine(directory, "payload.exe");
    await UpdateProtocol.DownloadAsync(client, verified, source, null, CancellationToken.None);
    Check(File.ReadAllBytes(source).SequenceEqual(body) && handler.Authorization is null && handler.Cookie is null, "verified download sends no session or cookies");
    UpdateFiles.VerifyPayload(source, manifest); Check(true, "payload length hash and PE marker verified");
    var target = Path.Combine(directory, "LexiFlow.exe");
    var old = Encoding.UTF8.GetBytes("MZ original executable"); File.WriteAllBytes(target, old);
    var userData = Path.Combine(directory, "learning-records.dat"); File.WriteAllText(userData, "unchanged user records");
    var nonce = Guid.NewGuid().ToString("N"); var oldHash = UpdateFiles.Hash(target);
    Denied(() => UpdateFiles.Replace(source, target, manifest, new string('0', 64), nonce), "wrong existing executable hash blocks replacement");
    Check(File.ReadAllBytes(target).SequenceEqual(old), "failed verification keeps original executable");
    var backup = UpdateFiles.Replace(source, target, manifest, oldHash, nonce);
    Check(File.ReadAllBytes(target).SequenceEqual(body) && File.ReadAllBytes(backup).SequenceEqual(old), "atomic replacement retains previous executable");
    Check(File.ReadAllText(userData) == "unchanged user records", "replacement never changes adjacent user records");
    UpdateFiles.Restore(target, backup, manifest, oldHash, nonce);
    Check(File.ReadAllBytes(target).SequenceEqual(old) && File.ReadAllText(userData) == "unchanged user records", "rollback restores executable without touching user records");
    File.WriteAllBytes(source, Encoding.UTF8.GetBytes("MZ changed payload"));
    Denied(() => UpdateFiles.Replace(source, target, manifest, oldHash, Guid.NewGuid().ToString("N")), "staged payload tampering rejected before replacement");
    File.WriteAllBytes(source, body);
    Denied(() => UpdateFiles.Replace(source, target, manifest, oldHash, "../escape"), "path traversal ticket rejected");
    Denied(() => UpdateFiles.DirectoryFor("../escape"), "cache path traversal rejected");
    Denied(() => UpdateFiles.Replace(source, userData, manifest, UpdateFiles.Hash(userData), Guid.NewGuid().ToString("N")), "non-executable replacement target rejected");
    var collisionNonce = Guid.NewGuid().ToString("N"); var collision = target + ".lexiflow-previous-" + collisionNonce;
    File.WriteAllText(collision, "do not overwrite");
    Denied(() => UpdateFiles.Replace(source, target, manifest, oldHash, collisionNonce), "existing unrelated backup file is not overwritten");
    Check(File.ReadAllText(collision) == "do not overwrite", "unrelated backup preserved");
    handler.Status = HttpStatusCode.Redirect;
    await DeniedAsync(() => UpdateProtocol.DownloadAsync(client, verified, Path.Combine(directory, "redirect.exe"), null, CancellationToken.None), "HTTP redirects rejected");
    handler.Status = HttpStatusCode.OK; handler.Body = body[..^1]; handler.DeclaredLength = body.Length;
    await DeniedAsync(() => UpdateProtocol.DownloadAsync(client, verified, Path.Combine(directory, "truncated.exe"), null, CancellationToken.None), "truncated download rejected");
    handler.Body = body.Concat(new byte[] { 1 }).ToArray();
    await DeniedAsync(() => UpdateProtocol.DownloadAsync(client, verified, Path.Combine(directory, "oversized.exe"), null, CancellationToken.None), "oversized streamed download rejected");
    handler.Body = body.ToArray(); handler.Body[^1] ^= 1;
    await DeniedAsync(() => UpdateProtocol.DownloadAsync(client, verified, Path.Combine(directory, "tampered.exe"), null, CancellationToken.None), "download digest mismatch rejected");
    handler.Body = body; handler.DeclaredLength = body.Length + 1;
    await DeniedAsync(() => UpdateProtocol.DownloadAsync(client, verified, Path.Combine(directory, "header.exe"), null, CancellationToken.None), "incorrect length header rejected");
    handler.DeclaredLength = body.Length; handler.Encoded = true;
    await DeniedAsync(() => UpdateProtocol.DownloadAsync(client, verified, Path.Combine(directory, "encoded.exe"), null, CancellationToken.None), "unexpected HTTP encoding rejected");
    handler.Encoded = false;
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    await DeniedAsync(() => UpdateProtocol.DownloadAsync(client, verified, Path.Combine(directory, "cancelled.exe"), null, cancelled.Token), "cancelled download cannot commit");
    await DeniedAsync(async () => { using var stream = new MemoryStream(new byte[16385]); _ = await UpdateProtocol.ReadBoundedAsync(stream, 16384, CancellationToken.None); }, "chunked feed size bounded");
    Check(File.ReadAllBytes(target).SequenceEqual(old), "all download failures leave old executable intact");
}
finally
{
    // This test created the exact flat directory; never recurse or touch app data.
    if (Path.GetFileName(directory).StartsWith("lexiflow-update-checks-", StringComparison.Ordinal))
    { foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory); }
}
Console.WriteLine($"{checks} update checks passed (ephemeral signing key, fake HTTP, invocation-owned files; no production writes).");

sealed class FakeDownload : HttpMessageHandler
{
    public byte[] Body { get; set; } = [];
    public long? DeclaredLength { get; set; }
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public bool Encoded { get; set; }
    public string? Authorization { get; private set; }
    public string? Cookie { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Authorization = request.Headers.Authorization?.ToString(); Cookie = request.Headers.TryGetValues("Cookie", out var values) ? string.Join(';', values) : null;
        var response = new HttpResponseMessage(Status) { Content = new ByteArrayContent(Body) };
        response.Content.Headers.ContentLength = DeclaredLength ?? Body.Length;
        if (Encoded) response.Content.Headers.ContentEncoding.Add("gzip");
        return Task.FromResult(response);
    }
}
