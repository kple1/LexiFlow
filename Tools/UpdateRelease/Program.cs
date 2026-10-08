using LexiFlow.Updates;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Signing key is protected to this Windows user.");
if (args.Length < 2) throw new ArgumentException("keygen KEY or sign KEY EXE VERSION OUTPUT");
var keyPath = Path.GetFullPath(args[1]);
if ((args[0] == "verify" && args.Length == 3) || (args[0] == "download-check" && args.Length == 4))
{
    if (new FileInfo(keyPath).Length > 16384) throw new InvalidDataException("Oversized feed.");
    var update = UpdateProtocol.Verify(File.ReadAllBytes(keyPath), UpdateTrust.PublicKey, UpdateProtocol.ParseVersion(args[2]), DateTimeOffset.UtcNow, false);
    if (args[0] == "download-check")
    {
        using var client = UpdateProtocol.CreateHttpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        await UpdateProtocol.DownloadAsync(client, update, Path.GetFullPath(args[3]), null, timeout.Token);
        UpdateFiles.VerifyPayload(Path.GetFullPath(args[3]), update.Manifest);
    }
    Console.WriteLine($"Verified {update.Manifest.Version}; {update.Manifest.Length} bytes; sha256={update.Manifest.Sha256}");
}
else if (args[0] == "keygen" && args.Length == 2)
{
    if (File.Exists(keyPath)) throw new IOException("Never overwrite a signing key.");
    Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var secret = key.ExportPkcs8PrivateKey();
    try
    {
        var encrypted = Dpapi.Transform(secret, true);
        using var file = new FileStream(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(encrypted); file.Flush(true);
        Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    }
    finally { CryptographicOperations.ZeroMemory(secret); }
}
else if (args[0] == "sign" && args.Length == 5)
{
    using var key = ECDsa.Create();
    var secret = Dpapi.Transform(File.ReadAllBytes(keyPath), false);
    try { key.ImportPkcs8PrivateKey(secret, out var consumed); if (consumed != secret.Length) throw new CryptographicException(); }
    finally { CryptographicOperations.ZeroMemory(secret); }
    var version = UpdateProtocol.ParseVersion(args[3]);
    var fileVersion = Version.Parse(FileVersionInfo.GetVersionInfo(Path.GetFullPath(args[2])).FileVersion ?? "");
    if (fileVersion.Major != version.Major || fileVersion.Minor != version.Minor || fileVersion.Build != version.Build || fileVersion.Revision != 0)
        throw new InvalidDataException("Executable version must match release version.");
    using var source = File.OpenRead(Path.GetFullPath(args[2]));
    if (source.ReadByte() != 'M' || source.ReadByte() != 'Z') throw new InvalidDataException("Not a Windows executable.");
    source.Position = 0;
    var now = DateTimeOffset.UtcNow;
    var manifest = new UpdateManifest { Schema = 1, Product = "LexiFlow", Platform = "windows-x64", Version = version.ToString(3),
        Url = UpdateProtocol.Origin + "/updates/windows-x64/" + version.ToString(3) + "/LexiFlow.exe", Length = source.Length,
        Sha256 = Convert.ToHexString(SHA256.HashData(source)), PublishedAt = now, ExpiresAt = now.AddDays(180) };
    var payload = JsonSerializer.SerializeToUtf8Bytes(manifest, UpdateProtocol.JsonOptions);
    var envelope = new UpdateEnvelope { Payload = Convert.ToBase64String(payload), Signature = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
    var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, UpdateProtocol.JsonOptions);
    UpdateProtocol.Verify(bytes, UpdateTrust.PublicKey, version, now, false);
    using var output = new FileStream(Path.GetFullPath(args[4]), FileMode.CreateNew, FileAccess.Write, FileShare.None);
    output.Write(bytes); output.Flush(true);
    Console.WriteLine($"Signed {version}; {manifest.Length} bytes; sha256={manifest.Sha256}");
}
else throw new ArgumentException("Invalid release command.");

internal static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    public static byte[] Transform(byte[] value, bool encrypt)
    {
        var input = new Blob { Size = value.Length, Data = Marshal.AllocHGlobal(value.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(value, 0, input.Data, value.Length);
            var ok = encrypt ? CryptProtectData(ref input, "LexiFlow release signing", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException("Signing key protection failed.");
            var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            Marshal.Copy(new byte[input.Size], 0, input.Data, input.Size); Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) { Marshal.Copy(new byte[output.Size], 0, output.Data, output.Size); LocalFree(output.Data); }
        }
    }
}
