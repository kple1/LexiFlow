using LexiFlow.Services;

namespace LexiFlow.Views;

public partial class AccountView : ContentPage
{
    private readonly SessionService _session;
    private readonly ApiService _api;
    private readonly StreakService _streak;
    private readonly LearningMetricsService _metrics;
    private readonly NotificationService _notifications;

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
        var confirm = await DisplayAlertAsync("로그아웃", "이 기기에서 로그아웃할까요?", "로그아웃", "취소");
        if (confirm)
            _session.SignOut();
    }
}
