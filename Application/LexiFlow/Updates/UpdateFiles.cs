using System.Security.Cryptography;

namespace LexiFlow.Updates;

public static class UpdateFiles
{
    public static string CacheRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LexiFlow", "ClientUpdates");
    public static string DirectoryFor(string nonce)
    {
        if (!Guid.TryParseExact(nonce, "N", out _)) throw new InvalidDataException("Invalid update ticket.");
        var directory = Path.Combine(CacheRoot, nonce);
        EnsureNoLinks(CacheRoot); EnsureNoLinks(directory);
        return directory;
    }
    public static void EnsureNoLinks(string path)
    {
        var current = Path.GetFullPath(path);
        if (current.StartsWith("\\\\", StringComparison.Ordinal)) throw new InvalidDataException("Network update paths are not allowed.");
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked update paths are not allowed.");
            current = Path.GetDirectoryName(current) ?? "";
        }
    }
    public static string Hash(string file)
    {
        EnsureNoLinks(file);
        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(input));
    }
    public static void VerifyPayload(string file, UpdateManifest manifest)
    {
        EnsureNoLinks(file);
        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length != manifest.Length || input.ReadByte() != 'M' || input.ReadByte() != 'Z')
            throw new InvalidDataException("Invalid executable payload.");
        input.Position = 0;
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(input), Convert.FromHexString(manifest.Sha256)))
            throw new CryptographicException("Update file changed.");
    }
    public static string Replace(string source, string target, UpdateManifest manifest, string oldHash, string nonce)
    {
        if (!Guid.TryParseExact(nonce, "N", out _) || Path.GetFileName(target) != "LexiFlow.exe"
            || !Path.IsPathFullyQualified(target) || Hash(target) != oldHash) throw new InvalidDataException("Invalid update target.");
        VerifyPayload(source, manifest);
        var incoming = target + ".lexiflow-new-" + nonce;
        var backup = target + ".lexiflow-previous-" + nonce;
        EnsureNoLinks(incoming); EnsureNoLinks(backup);
        if (File.Exists(incoming) || File.Exists(backup)) throw new IOException("Update paths already exist.");
        var copied = false;
        try
        {
            File.Copy(source, incoming, false); copied = true;
            VerifyPayload(incoming, manifest);
            if (Hash(target) != oldHash) throw new InvalidDataException("Running executable changed.");
            File.Replace(incoming, target, backup, false);
            return backup;
        }
        finally { if (copied && File.Exists(incoming)) File.Delete(incoming); }
    }
    public static void Restore(string target, string backup, UpdateManifest manifest, string oldHash, string nonce)
    {
        if (backup != target + ".lexiflow-previous-" + nonce || Hash(backup) != oldHash) throw new InvalidDataException("Invalid rollback backup.");
        VerifyPayload(target, manifest);
        var failed = target + ".lexiflow-failed-" + nonce;
        EnsureNoLinks(failed);
        if (File.Exists(failed)) throw new IOException("Rollback path already exists.");
        File.Replace(backup, target, failed, false);
        File.Delete(failed); // Only the failed signed payload from this transaction.
    }
}
