using LexiFlow.Services;

namespace LexiFlow.Views;

public partial class AccountView : ContentPage
{
    private readonly SessionService _session;
    private readonly ApiService _api;
    private readonly StreakService _streak;
    private readonly LearningMetricsService _metrics;
    private readonly NotificationService _notifications;
    private bool _securityBusy;

    public AccountView(
        SessionService session,
        ApiService api,
        StreakService streak,
        LearningMetricsService metrics,
        NotificationService notifications)
    {
        InitializeComponent();
        _session = session;
        _api = api;
        _streak = streak;
        _metrics = metrics;
        _notifications = notifications;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateLocalMetrics();
        await Task.WhenAll(LoadStatsAsync(), LoadReminderStateAsync());
    }

    private void UpdateLocalMetrics()
    {
        var user = _session.CurrentUserId ?? "-";
        userLabel.Text = user;
        avatarLabel.Text = user.Length > 0 ? user[..1].ToUpperInvariant() : "L";
        accountLevelLabel.Text = $"LEVEL {_metrics.Level} · {_metrics.TotalXp:N0} XP";
        streakLabel.Text = $"{_streak.Current}일";
        dailyGoalLabel.Text = $"{_metrics.TodayReviews} / {_metrics.DailyGoal}문제";
        dailyGoalProgress.Progress = _metrics.DailyGoalProgress;
        dailyGoalStatusLabel.Text = _metrics.TodayReviews >= _metrics.DailyGoal ? "달성 완료" : "진행 중";
        dailyGoalStatusLabel.TextColor = _metrics.TodayReviews >= _metrics.DailyGoal
            ? Color.FromArgb("#22C55E")
            : Color.FromArgb("#5B8CFF");
        levelProgressLabel.Text = $"다음 레벨까지 {_metrics.XpToNextLevel} XP";
    }

    private async Task LoadStatsAsync()
    {
        if (!_session.IsLoggedIn)
            return;

        loadingOverlay.IsVisible = true;
        errorBorder.IsVisible = false;

        try
        {
            var userId = _session.CurrentUserId!;
            var wordsTask = _api.GetProgressAsync(userId);
            var grammarTask = _api.GetGrammarProgressAsync(userId);
            var idiomTask = _api.GetIdiomProgressAsync(userId);
            await Task.WhenAll(wordsTask, grammarTask, idiomTask);

            var statuses = (await wordsTask).Select(item => item.Status)
                .Concat((await grammarTask).Select(item => item.Status))
                .Concat((await idiomTask).Select(item => item.Status))
                .ToList();

            masteredLabel.Text = statuses.Count(status => status == "Mastered").ToString();
            learningLabel.Text = statuses.Count(status => status == "Learning").ToString();
            totalLabel.Text = $"총 {statuses.Count}개 학습";
        }
        catch
        {
            errorBorder.IsVisible = true;
        }
        finally
        {
            loadingOverlay.IsVisible = false;
        }
    }

    private async Task LoadReminderStateAsync()
    {
        var enabled = await _notifications.AreNotificationsEnabledAsync();
        reminderButton.Text = enabled ? "켜짐" : "켜기";
        reminderButton.IsEnabled = !enabled;
        reminderStatusLabel.Text = enabled
            ? "내일부터 매일 오전 9시에 알려드릴게요."
            : "알림 권한은 원할 때만 요청해요.";
    }

    private async void OnEnableReminderClick(object? sender, EventArgs e)
    {
        reminderButton.IsEnabled = false;
        reminderButton.Text = "설정 중";

        var enabled = await _notifications.EnableDailyReminderAsync();
        reminderButton.Text = enabled ? "켜짐" : "다시 시도";
        reminderButton.IsEnabled = !enabled;
        reminderStatusLabel.Text = enabled
            ? "내일부터 매일 오전 9시에 알려드릴게요."
            : "알림 권한이 꺼져 있어요. 시스템 설정을 확인해 주세요.";
    }

    private async void OnSignOutClick(object? sender, EventArgs e)
    {
        if (_securityBusy) return;
        var confirm = await DisplayAlertAsync("로그아웃", "이 기기에서 로그아웃할까요?", "로그아웃", "취소");
        if (!confirm) return;
        try { await _api.LogoutAsync(); }
        catch
        {
            await DisplayAlertAsync("기기에서 로그아웃", "서버에 연결하지 못해 서버 세션은 즉시 폐기되지 않았습니다. 세션은 발급 후 최대 7일에 만료됩니다.", "확인");
        }
        finally { _session.SignOut(); }
    }

    private async void OnChangePasswordClick(object? sender, EventArgs e)
    {
        if (_securityBusy) return;
        var current = currentPasswordEntry.Text ?? "";
        var next = newPasswordEntry.Text ?? "";
        if (string.IsNullOrEmpty(current) || next.Length < 12 || System.Text.Encoding.UTF8.GetByteCount(next) > 72)
        {
            await DisplayAlertAsync("입력 확인", "현재 비밀번호와 새 비밀번호를 입력해 주세요. 새 비밀번호는 12자 이상, UTF-8 72바이트 이내여야 합니다.", "확인");
            return;
        }
        if (!await DisplayAlertAsync("비밀번호 변경", "변경 후 모든 기기에서 로그아웃됩니다. 계속할까요?", "변경", "취소")) return;
        _securityBusy = true;
        securityActions.IsEnabled = false;
        try { await _api.ChangePasswordAsync(current, next); }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        { await DisplayAlertAsync("변경하지 못했어요", "현재 비밀번호를 확인해 주세요.", "확인"); }
        catch { await DisplayAlertAsync("변경하지 못했어요", "연결과 로그인 상태를 확인한 뒤 다시 시도해 주세요.", "확인"); }
        finally
        {
            currentPasswordEntry.Text = newPasswordEntry.Text = "";
            _securityBusy = false;
            securityActions.IsEnabled = true;
        }
    }

    private async void OnDeleteAccountClick(object? sender, EventArgs e)
    {
        if (_securityBusy) return;
        var current = currentPasswordEntry.Text ?? "";
        if (string.IsNullOrEmpty(current))
        {
            await DisplayAlertAsync("비밀번호 확인", "현재 비밀번호를 먼저 입력해 주세요.", "확인");
            return;
        }
        if (!await DisplayAlertAsync("계정을 삭제할까요?",
            "서버 계정·학습 기록과 이 기기의 Archive·코스·XP를 삭제합니다. 복구할 수 없습니다. 다른 기기에 남은 로컬 데이터는 그 기기에서 별도로 삭제해야 합니다.",
            "계정 삭제", "취소")) return;
        _securityBusy = true;
        securityActions.IsEnabled = false;
        try
        {
            await _api.DeleteAccountAsync(current);
            try { LocalAccountData.DeleteCurrent(_session); }
            finally { _session.SignOut(); }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        { await DisplayAlertAsync("삭제하지 못했어요", "현재 비밀번호를 확인해 주세요.", "확인"); }
        catch { await DisplayAlertAsync("삭제 상태 확인 필요", "연결이 끊겼거나 일부 기기 데이터를 지우지 못했습니다. 다시 로그인해 계정 상태를 확인해 주세요.", "확인"); }
        finally
        {
            currentPasswordEntry.Text = newPasswordEntry.Text = "";
            _securityBusy = false;
            securityActions.IsEnabled = true;
        }
    }
}
