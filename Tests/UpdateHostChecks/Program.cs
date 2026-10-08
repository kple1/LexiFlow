using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using LexiFlow.Updates;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Real Windows helper checks require Windows.");
if (args is ["--run-helper-checks", var projectPath])
{
    await RunChecks(Path.GetFullPath(projectPath));
    return;
}
if (UpdateBootstrap.TryRunHelper()) return;
if (args is ["--fixture-parent", var stop])
{
    while (!File.Exists(stop)) await Task.Delay(100);
    return;
}
if (args is ["--lexiflow-update-started", _])
{
    if (UpdateTrust.Metadata("TestCrash") == "true") Environment.Exit(7);
    UpdateBootstrap.SignalHealthy();
}

static async Task RunChecks(string project)
{
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    var root = Path.Combine(Path.GetTempPath(), "lexiflow-helper-checks-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var cacheDirectories = new List<string>();
    var checks = 0;
    void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL " + name);
        checks++; Console.WriteLine("PASS " + name);
    }
    async Task<string> Publish(string label, string version, bool crash)
    {
        var directory = Path.Combine(root, label);
        var command = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        foreach (var value in new[] { "publish", project, "-c", "Release", "-r", "win-x64", "--self-contained", "true",
            "-p:PublishSingleFile=true", "-p:DebugType=None", "-p:DebugSymbols=false", "-p:TestVersion=" + version,
            "-p:TestPublicKey=" + publicKey, "-p:TestCrash=" + crash.ToString().ToLowerInvariant(), "-o", directory, "-v", "quiet" }) command.ArgumentList.Add(value);
        using var process = Process.Start(command)!; await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new Exception("Fixture publish failed.");
        return Path.Combine(directory, "LexiFlow.exe");
    }
    try
    {
        var old = await Publish("old", "1.0.0", false);
        var good = await Publish("good", "1.0.1", false);
        var bad = await Publish("crashing", "1.0.2", true);
        foreach (var scenario in new[] { (Name: "upgrade", File: good, Version: "1.0.1", Success: true), (Name: "rollback", File: bad, Version: "1.0.2", Success: false) })
        {
            var directory = Path.Combine(root, scenario.Name); Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, "LexiFlow.exe"); File.Copy(old, target);
            var records = Path.Combine(directory, "learning-records.dat"); File.WriteAllText(records, "unchanged learning records");
            var stop = Path.Combine(directory, "stop-parent");
            var startParent = new ProcessStartInfo(target) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
            startParent.ArgumentList.Add("--fixture-parent"); startParent.ArgumentList.Add(stop);
            using var parent = Process.Start(startParent)!;
            var nonce = Guid.NewGuid().ToString("N");
            var cache = UpdateFiles.DirectoryFor(nonce); Directory.CreateDirectory(cache); cacheDirectories.Add(cache);
            File.WriteAllText(Path.Combine(cache, "owner"), "LexiFlow.ClientUpdate.v1");
            File.Copy(old, Path.Combine(cache, "runner.exe")); File.Copy(scenario.File, Path.Combine(cache, "payload.exe"));
            var manifest = new UpdateManifest { Schema = 1, Product = "LexiFlow", Platform = "windows-x64", Version = scenario.Version,
                Url = UpdateProtocol.Origin + "/updates/windows-x64/" + scenario.Version + "/LexiFlow.exe", Length = new FileInfo(scenario.File).Length,
                Sha256 = UpdateFiles.Hash(scenario.File), PublishedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };
            var payload = JsonSerializer.SerializeToUtf8Bytes(manifest, UpdateProtocol.JsonOptions);
            var envelope = JsonSerializer.SerializeToUtf8Bytes(new UpdateEnvelope { Payload = Convert.ToBase64String(payload),
                Signature = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) }, UpdateProtocol.JsonOptions);
            var ticket = new UpdateTicket { Nonce = nonce, TargetPath = target, OldHash = UpdateFiles.Hash(old), CurrentVersion = "1.0.0",
                ParentPid = parent.Id, ParentStartTicks = parent.StartTime.ToUniversalTime().Ticks, Envelope = Convert.ToBase64String(envelope) };
            File.WriteAllBytes(Path.Combine(cache, "request.json"), JsonSerializer.SerializeToUtf8Bytes(ticket, UpdateProtocol.JsonOptions));
            var startHelper = new ProcessStartInfo(Path.Combine(cache, "runner.exe")) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
            startHelper.ArgumentList.Add("--lexiflow-apply-update"); startHelper.ArgumentList.Add(nonce);
            using var helper = Process.Start(startHelper)!;
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                while (!File.Exists(Path.Combine(cache, "ready")) && !helper.HasExited) await Task.Delay(100, deadline.Token);
                Check(!helper.HasExited && File.ReadAllText(Path.Combine(cache, "ready")) == nonce, scenario.Name + ": verified helper ready before parent exits");
                Check(UpdateFiles.Hash(target) == ticket.OldHash && !parent.HasExited, scenario.Name + ": live executable not replaced");
                File.WriteAllText(stop, "exit"); await parent.WaitForExitAsync(deadline.Token); await helper.WaitForExitAsync(deadline.Token);
                Check(helper.ExitCode == (scenario.Success ? 0 : 2), scenario.Name + ": expected helper exit status");
                Check(UpdateFiles.Hash(target) == (scenario.Success ? manifest.Sha256 : ticket.OldHash), scenario.Name + ": correct executable at unchanged target path");
                Check(File.ReadAllText(records) == "unchanged learning records", scenario.Name + ": learning records preserved");
                using var result = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(cache, "result.json")));
                Check(result.RootElement.GetProperty("State").GetString() == (scenario.Success ? "complete" : "failed"), scenario.Name + ": success/failure receipt recorded");
                if (scenario.Success) Check(UpdateFiles.Hash(target + ".lexiflow-previous-" + nonce) == ticket.OldHash, "upgrade: old binary retained for manual recovery");
                else Check(!File.Exists(target + ".lexiflow-previous-" + nonce), "rollback: previous binary restored atomically");
            }
            finally
            {
                if (!parent.HasExited) { File.WriteAllText(stop, "exit"); await parent.WaitForExitAsync(); }
                if (!helper.HasExited) { helper.Kill(false); await helper.WaitForExitAsync(); }
            }
        }
        Console.WriteLine($"{checks} real helper checks passed (isolated fixture binaries and ephemeral signing key; no production database or user storage).");
    }
    finally
    {
        // Delete only files inside directories created by this invocation; never recurse into unknown paths.
        foreach (var directory in cacheDirectories.Concat(Directory.EnumerateDirectories(root)))
        {
            UpdateFiles.EnsureNoLinks(directory);
            foreach (var file in Directory.EnumerateFiles(directory)) { UpdateFiles.EnsureNoLinks(file); File.Delete(file); }
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
        if (!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
    }
}
