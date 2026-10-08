using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LexiFlow.Updates;

public sealed record UpdateTicket
{
    [JsonRequired] public string Nonce { get; init; } = "";
    [JsonRequired] public string TargetPath { get; init; } = "";
    [JsonRequired] public string OldHash { get; init; } = "";
    [JsonRequired] public string CurrentVersion { get; init; } = "";
    [JsonRequired] public int ParentPid { get; init; }
    [JsonRequired] public long ParentStartTicks { get; init; }
    [JsonRequired] public string Envelope { get; init; } = "";
}

public static class UpdateBootstrap
{
    private static readonly string[] ManagedFiles = ["payload.exe", "runner.exe", "request.json", "manifest.json", "ready", "healthy", "result.json", "owner"];
    private static UpdateTicket ReadTicket(string directory)
    {
        var path = Path.Combine(directory, "request.json");
        UpdateFiles.EnsureNoLinks(path);
        if (new FileInfo(path).Length > 49152) throw new InvalidDataException("Oversized update request.");
        var ticket = UpdateProtocol.ReadStrict<UpdateTicket>(File.ReadAllBytes(path));
        if (UpdateFiles.DirectoryFor(ticket.Nonce) != directory || ticket.OldHash is not { Length: 64 }
            || !ticket.OldHash.All(char.IsAsciiHexDigit) || ticket.ParentPid <= 0) throw new InvalidDataException("Invalid update request.");
        return ticket;
    }

    public static bool TryRunHelper()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (args.Length == 0 || args[0] != "--lexiflow-apply-update") return false;
        if (!OperatingSystem.IsWindows() || args.Length != 2) { Environment.Exit(2); return true; }
        var exit = Task.Run(() => RunHelperAsync(args[1])).GetAwaiter().GetResult();
        Environment.Exit(exit); return true;
    }

    private static async Task<int> RunHelperAsync(string nonce)
    {
        string? directory = null;
        string? backup = null;
        UpdateTicket? ticket = null;
        VerifiedUpdate? update = null;
        Process? child = null;
        var parentExited = false;
        try
        {
            directory = UpdateFiles.DirectoryFor(nonce);
            if (Environment.ProcessPath != Path.Combine(directory, "runner.exe")) throw new InvalidDataException("Invalid helper process.");
            ticket = ReadTicket(directory);
            if (UpdateProtocol.ParseVersion(ticket.CurrentVersion) != UpdateTrust.CurrentVersion
                || UpdateFiles.Hash(Environment.ProcessPath!) != ticket.OldHash || UpdateFiles.Hash(ticket.TargetPath) != ticket.OldHash)
                throw new InvalidDataException("Helper/target mismatch.");
            update = UpdateProtocol.Verify(Convert.FromBase64String(ticket.Envelope), UpdateTrust.PublicKey, UpdateTrust.CurrentVersion, DateTimeOffset.UtcNow);
            UpdateFiles.VerifyPayload(Path.Combine(directory, "payload.exe"), update.Manifest);
            using var parent = Process.GetProcessById(ticket.ParentPid);
            if (parent.StartTime.ToUniversalTime().Ticks != ticket.ParentStartTicks
                || !string.Equals(parent.MainModule?.FileName, ticket.TargetPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Parent identity changed.");
            UpdateFiles.EnsureNoLinks(ticket.TargetPath);
            var probe = ticket.TargetPath + ".lexiflow-probe-" + nonce;
            using (var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Flush(true); }
            File.Delete(probe);
            File.WriteAllText(Path.Combine(directory, "ready"), nonce);
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90))) await parent.WaitForExitAsync(timeout.Token);
            parentExited = true;
            // Serialize all updates to this exact executable across app instances.
            var lockName = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ticket.TargetPath.ToUpperInvariant())));
            using var applyLock = new FileStream(Path.Combine(UpdateFiles.CacheRoot, lockName + ".apply.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try
            {
                backup = UpdateFiles.Replace(Path.Combine(directory, "payload.exe"), ticket.TargetPath, update.Manifest, ticket.OldHash, nonce);
                var start = new ProcessStartInfo(ticket.TargetPath) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(ticket.TargetPath)! };
                start.ArgumentList.Add("--lexiflow-update-started"); start.ArgumentList.Add(nonce);
                child = Process.Start(start) ?? throw new IOException("New application did not start.");
                var childStart = child.StartTime.ToUniversalTime().Ticks;
                var deadline = DateTime.UtcNow.AddSeconds(90);
                while (DateTime.UtcNow < deadline && !child.HasExited && !File.Exists(Path.Combine(directory, "healthy"))) await Task.Delay(250);
                if (!File.Exists(Path.Combine(directory, "healthy")))
                {
                    if (!child.HasExited && child.StartTime.ToUniversalTime().Ticks == childStart) { child.Kill(false); await child.WaitForExitAsync(); }
                    throw new IOException("New application did not complete startup.");
                }
                if (File.ReadAllText(Path.Combine(directory, "healthy")) != update.Manifest.Sha256) throw new InvalidDataException("Startup receipt mismatch.");
                File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { State = "complete", Target = ticket.TargetPath, Backup = backup, OldHash = ticket.OldHash }));
                File.Delete(Path.Combine(directory, "payload.exe"));
            }
            finally { applyLock.Dispose(); }
            return 0;
        }
        catch
        {
            if (backup is not null && ticket is not null && update is not null)
            {
                try
                {
                    if (child is not null && !child.HasExited) return 3; // Never overwrite a live application.
                    UpdateFiles.Restore(ticket.TargetPath, backup, update.Manifest, ticket.OldHash, nonce);
                    Process.Start(new ProcessStartInfo(ticket.TargetPath) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
                }
                catch { return 4; } // Keep backup for manual recovery; never touch user data.
            }
            if (directory is not null)
                try { File.WriteAllText(Path.Combine(directory, "result.json"), "{\"State\":\"failed\"}"); } catch { }
            if (parentExited && backup is null && ticket is not null)
                try { if (UpdateFiles.Hash(ticket.TargetPath) == ticket.OldHash) Process.Start(new ProcessStartInfo(ticket.TargetPath) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden }); } catch { }
            return 2;
        }
        finally { child?.Dispose(); }
    }

    public static void SignalHealthy()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (args.Length != 2 || args[0] != "--lexiflow-update-started") return;
        try
        {
            var directory = UpdateFiles.DirectoryFor(args[1]);
            var ticket = ReadTicket(directory);
            var update = UpdateProtocol.Verify(Convert.FromBase64String(ticket.Envelope), UpdateTrust.PublicKey, UpdateTrust.CurrentVersion, DateTimeOffset.UtcNow, false);
            if (update.Version != UpdateTrust.CurrentVersion || !string.Equals(Environment.ProcessPath, ticket.TargetPath, StringComparison.OrdinalIgnoreCase)) return;
            UpdateFiles.VerifyPayload(ticket.TargetPath, update.Manifest);
            File.WriteAllText(Path.Combine(directory, "healthy"), update.Manifest.Sha256);
        }
        catch { /* No acknowledgement means the helper retains/recoveries the previous version. */ }
    }

    public static void CleanupCompleted()
    {
        if (!Directory.Exists(UpdateFiles.CacheRoot)) return;
        UpdateFiles.EnsureNoLinks(UpdateFiles.CacheRoot);
        var completed = new List<(string Directory, DateTime Time)>();
        foreach (var dir in Directory.EnumerateDirectories(UpdateFiles.CacheRoot))
        {
            try
            {
                if (UpdateFiles.DirectoryFor(Path.GetFileName(dir)) != dir || !File.Exists(Path.Combine(dir, "owner")) || !File.Exists(Path.Combine(dir, "result.json"))) continue;
                using var result = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir, "result.json")));
                if (result.RootElement.GetProperty("State").GetString() == "complete") completed.Add((dir, Directory.GetLastWriteTimeUtc(dir)));
            }
            catch { }
        }
        foreach (var item in completed.OrderByDescending(i => i.Time).Skip(1))
        {
            try
            {
                var ticket = ReadTicket(item.Directory);
                var backup = ticket.TargetPath + ".lexiflow-previous-" + ticket.Nonce;
                if (string.Equals(ticket.TargetPath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(backup) && UpdateFiles.Hash(backup) == ticket.OldHash) File.Delete(backup);
            }
            catch { }
            // Only known cache files, no recursion and no deletion of adjacent user files.
            foreach (var name in ManagedFiles)
                try { var file = Path.Combine(item.Directory, name); UpdateFiles.EnsureNoLinks(file); if (File.Exists(file)) File.Delete(file); } catch { }
            try { if (!Directory.EnumerateFileSystemEntries(item.Directory).Any()) Directory.Delete(item.Directory); } catch { }
        }
    }
}
