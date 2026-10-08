using LexiFlow.Updates;

namespace LexiFlow.Views.UserControls;

public partial class AppUpdatePanel : ContentView
{
    private readonly AppUpdateService _updates = AppUpdateService.Default;
    private bool _loaded;
    public AppUpdatePanel()
    {
        InitializeComponent();
        Loaded += (_, _) => { if (_loaded) return; _loaded = true; _updates.Changed += OnChanged; Render(); };
        Unloaded += (_, _) => { _loaded = false; _updates.Changed -= OnChanged; };
    }
    private void OnChanged(object? sender, EventArgs e) => MainThread.BeginInvokeOnMainThread(() => { if (_loaded) Render(); });
    private void Render()
    {
        versionLabel.Text = "v" + _updates.CurrentVersion;
        updateStatusLabel.Text = _updates.Message;
        checkButton.IsEnabled = OperatingSystem.IsWindows() && !_updates.IsBusy;
        downloadButton.IsVisible = _updates.CanDownload;
        applyButton.IsVisible = _updates.CanApply;
        downloadProgress.IsVisible = cancelButton.IsVisible = _updates.State == AppUpdateState.Downloading;
        downloadProgress.Progress = _updates.Progress;
    }
    private async void OnCheck(object? sender, EventArgs e) => await _updates.CheckAsync();
    private async void OnDownload(object? sender, EventArgs e) => await _updates.DownloadAsync();
    private void OnCancel(object? sender, EventArgs e) => _updates.CancelDownload();
    private async void OnApply(object? sender, EventArgs e)
    {
        Element? element = this;
        while (element is not null && element is not Page) element = element.Parent;
        if (element is not Page page || !_updates.CanApply) return;
        if (!await page.DisplayAlertAsync("업데이트를 적용할까요?", "진행 중인 학습·계정 작업을 마친 뒤 적용하세요. 앱을 재시작하며 저장된 학습 기록은 유지합니다.", "재시작", "나중에")) return;
        if (await _updates.PrepareRestartAsync()) Application.Current?.Quit();
    }
}
