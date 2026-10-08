using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace LexiFlow.Updates;

public enum AppUpdateState { Idle, Checking, Available, Downloading, Ready, Applying, Error, Unsupported }

public sealed class AppUpdateService
{
    public static AppUpdateService Default { get; } = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _http = UpdateProtocol.CreateHttpClient();
    private VerifiedUpdate? _update;
    private string? _directory;
    private CancellationTokenSource? _downloadCancellation;
    public event EventHandler? Changed;
    public AppUpdateState State { get; private set; } = OperatingSystem.IsWindows() ? AppUpdateState.Idle : AppUpdateState.Unsupported;
    public string Message { get; private set; } = OperatingSystem.IsWindows() ? "업데이트 확인을 기다리는 중" : "Windows에서 앱 업데이트를 지원합니다.";
    public string CurrentVersion => UpdateTrust.CurrentVersion.ToString(3);
    public double Progress { get; private set; }
    public bool IsBusy => State is AppUpdateState.Checking or AppUpdateState.Downloading or AppUpdateState.Applying;
    public bool CanDownload => _update is not null && _directory is null && !IsBusy;
    public bool CanApply => _update is not null && _directory is not null && !IsBusy;
    private void Set(AppUpdateState state, string message) { State = state; Message = message; Changed?.Invoke(this, EventArgs.Empty); }

    public async Task CheckAsync()
    {
        if (!OperatingSystem.IsWindows() || !await _gate.WaitAsync(0)) return;
        try
        {
            if (_directory is not null) { Set(AppUpdateState.Ready, "다운로드 완료. 준비되면 재시작해 적용하세요."); return; }
            Set(AppUpdateState.Checking, "새 버전 확인 중…");
            try { UpdateBootstrap.CleanupCompleted(); } catch { }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var response = await _http.GetAsync(UpdateProtocol.FeedUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException("Update feed unavailable.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var bytes = await UpdateProtocol.ReadBoundedAsync(stream, 16384, timeout.Token);
            var candidate = UpdateProtocol.Verify(bytes, UpdateTrust.PublicKey, UpdateTrust.CurrentVersion, DateTimeOffset.UtcNow, false);
            if (candidate.Version < UpdateTrust.CurrentVersion) throw new InvalidDataException("Downgrade feed.");
            _update = candidate.Version > UpdateTrust.CurrentVersion ? candidate : null;
            if (_update is not null && await Task.Run(() => ResumeDownload(_update)))
            {
                Progress = 1;
                Set(AppUpdateState.Ready, "이전에 받은 업데이트를 검증했습니다. 준비되면 재시작하세요.");
                return;
            }
            Set(_update is null ? AppUpdateState.Idle : AppUpdateState.Available, _update is null
                ? $"최신 버전입니다 · {CurrentVersion}" : $"{candidate.Manifest.Version} 사용 가능 · {candidate.Manifest.Length / (1024d * 1024):N0} MB");
        }
        catch (Exception error) { _update = null; Set(AppUpdateState.Error, ErrorMessage(error)); }
        finally { _gate.Release(); }
    }

    public void CancelDownload() => _downloadCancellation?.Cancel();
    public async Task DownloadAsync()
    {
        if (!OperatingSystem.IsWindows() || !CanDownload || !await _gate.WaitAsync(0)) return;
        string? directory = null;
        try
        {
            var update = _update!;
            // Re-validate expiration immediately before spending bandwidth.
            UpdateProtocol.Verify(update.Envelope, UpdateTrust.PublicKey, UpdateTrust.CurrentVersion, DateTimeOffset.UtcNow);
            directory = UpdateFiles.DirectoryFor(Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(UpdateFiles.CacheRoot);
            UpdateFiles.EnsureNoLinks(UpdateFiles.CacheRoot);
            using var downloadLock = new FileStream(Path.Combine(UpdateFiles.CacheRoot, "download.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var drive = new DriveInfo(Path.GetPathRoot(directory)!);
            if (drive.AvailableFreeSpace < update.Manifest.Length * 4 + 100 * 1024 * 1024) throw new IOException("Not enough space for safe update.");
            Directory.CreateDirectory(directory); UpdateFiles.EnsureNoLinks(directory);
            File.WriteAllText(Path.Combine(directory, "owner"), "LexiFlow.ClientUpdate.v1");
            _downloadCancellation = new(TimeSpan.FromMinutes(10));
            Progress = 0; Set(AppUpdateState.Downloading, "업데이트 다운로드 중…");
            var reported = -1d;
            var progress = new Progress<double>(value =>
            {
                if (State != AppUpdateState.Downloading || value < reported + 0.01 && value < 1) return;
                reported = value; Progress = value; Changed?.Invoke(this, EventArgs.Empty);
            });
            await UpdateProtocol.DownloadAsync(_http, update, Path.Combine(directory, "payload.exe"), progress, _downloadCancellation.Token);
            UpdateFiles.VerifyPayload(Path.Combine(directory, "payload.exe"), update.Manifest);
            File.WriteAllBytes(Path.Combine(directory, "manifest.json"), update.Envelope);
            _directory = directory;
            Progress = 1; Set(AppUpdateState.Ready, "다운로드 검증 완료. 재시작하면 적용됩니다.");
        }
        catch (Exception error)
        {
            if (directory is not null && _directory != directory)
                foreach (var name in new[] { "payload.exe", "manifest.json", "owner" })
                    try { var file = Path.Combine(directory, name); UpdateFiles.EnsureNoLinks(file); if (File.Exists(file)) File.Delete(file); } catch { }
            Set(AppUpdateState.Error, error is OperationCanceledException ? "다운로드가 중단됐습니다. 기존 버전은 그대로입니다." : ErrorMessage(error));
        }
        finally { _downloadCancellation?.Dispose(); _downloadCancellation = null; _gate.Release(); }
    }

    public async Task<bool> PrepareRestartAsync()
    {
        if (!OperatingSystem.IsWindows() || !CanApply || !await _gate.WaitAsync(0)) return false;
        Process? helper = null;
        try
        {
            Set(AppUpdateState.Applying, "재시작 준비 중…");
            var directory = _directory!;
            var nonce = Path.GetFileName(directory);
            if (UpdateFiles.DirectoryFor(nonce) != directory) throw new InvalidDataException();
            var target = Environment.ProcessPath ?? throw new IOException("Unknown executable.");
            if (Path.GetFileName(target) != "LexiFlow.exe") throw new IOException("Keep the executable name LexiFlow.exe.");
            UpdateFiles.EnsureNoLinks(target);
            if (new DriveInfo(Path.GetPathRoot(target)!).AvailableFreeSpace < _update!.Manifest.Length * 2 + 100 * 1024 * 1024)
                throw new IOException("Not enough space on the application drive.");
            foreach (var process in Process.GetProcessesByName("LexiFlow"))
                using (process)
                {
                    if (process.Id == Environment.ProcessId) continue;
                    try { if (string.Equals(process.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase)) throw new IOException("Close other instances first."); }
                    catch (System.ComponentModel.Win32Exception) { }
                }
            using var current = Process.GetCurrentProcess();
            var ticket = new UpdateTicket { Nonce = nonce, TargetPath = target, OldHash = UpdateFiles.Hash(target), CurrentVersion = CurrentVersion,
                ParentPid = current.Id, ParentStartTicks = current.StartTime.ToUniversalTime().Ticks, Envelope = Convert.ToBase64String(_update!.Envelope) };
            var runner = Path.Combine(directory, "runner.exe");
            if (File.Exists(runner)) { if (UpdateFiles.Hash(runner) != ticket.OldHash) throw new InvalidDataException(); }
            else await Task.Run(() => File.Copy(target, runner, false));
            foreach (var name in new[] { "ready", "healthy", "result.json" })
            { var file = Path.Combine(directory, name); UpdateFiles.EnsureNoLinks(file); if (File.Exists(file)) File.Delete(file); }
            File.WriteAllBytes(Path.Combine(directory, "request.json"), JsonSerializer.SerializeToUtf8Bytes(ticket, UpdateProtocol.JsonOptions));
            var start = new ProcessStartInfo(runner) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = directory };
            start.ArgumentList.Add("--lexiflow-apply-update"); start.ArgumentList.Add(nonce);
            helper = Process.Start(start) ?? throw new IOException("Updater did not start.");
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (!helper.HasExited && DateTime.UtcNow < deadline)
            {
                if (File.Exists(Path.Combine(directory, "ready")) && File.ReadAllText(Path.Combine(directory, "ready")) == nonce) return true;
                await Task.Delay(200);
            }
            throw new IOException("Updater could not prepare safely.");
        }
        catch (Exception error)
        {
            if (helper is not null && !helper.HasExited) { helper.Kill(false); await helper.WaitForExitAsync(); }
            Set(AppUpdateState.Error, ErrorMessage(error)); return false;
        }
        finally { helper?.Dispose(); _gate.Release(); }
    }

    private bool ResumeDownload(VerifiedUpdate candidate)
    {
        if (!Directory.Exists(UpdateFiles.CacheRoot)) return false;
        UpdateFiles.EnsureNoLinks(UpdateFiles.CacheRoot);
        foreach (var directory in Directory.EnumerateDirectories(UpdateFiles.CacheRoot))
        {
            try
            {
                if (UpdateFiles.DirectoryFor(Path.GetFileName(directory)) != directory
                    || File.Exists(Path.Combine(directory, "request.json")) || File.Exists(Path.Combine(directory, "result.json"))) continue;
                var owner = Path.Combine(directory, "owner");
                var manifest = Path.Combine(directory, "manifest.json");
                UpdateFiles.EnsureNoLinks(owner); UpdateFiles.EnsureNoLinks(manifest);
                if (new FileInfo(owner).Length > 64 || File.ReadAllText(owner) != "LexiFlow.ClientUpdate.v1"
                    || new FileInfo(manifest).Length > 16384) continue;
                var cached = UpdateProtocol.Verify(File.ReadAllBytes(manifest), UpdateTrust.PublicKey, UpdateTrust.CurrentVersion, DateTimeOffset.UtcNow);
                if (cached.Version != candidate.Version || cached.Manifest.Sha256 != candidate.Manifest.Sha256) continue;
                UpdateFiles.VerifyPayload(Path.Combine(directory, "payload.exe"), cached.Manifest);
                _directory = directory;
                return true;
            }
            catch { /* Invalid/incomplete caches never replace the running app. */ }
        }
        return false;
    }

    private static string ErrorMessage(Exception error) => error switch
    {
        CryptographicException or InvalidDataException or JsonException or FormatException => "업데이트 검증에 실패했습니다. 기존 버전은 유지됩니다.",
        IOException or UnauthorizedAccessException => "업데이트를 준비하지 못했습니다. 여유 공간·폴더 권한·다른 실행 창을 확인하세요.",
        _ => "업데이트 서버에 연결하지 못했습니다. 기존 앱은 계속 사용할 수 있습니다."
    };
}
