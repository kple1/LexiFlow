using LexiFlow.Models;
using LexiFlow.Services;

namespace LexiFlow.Views;

public partial class HomeView : ContentPage
{
    private readonly ApiService _api;
    private readonly SessionService _session;
    private readonly StreakService _streak;
    private readonly LearningMetricsService _metrics;

    public HomeView(
        ApiService api,
        SessionService session,
        StreakService streak,
        LearningMetricsService metrics)
    {
        InitializeComponent();
        _api = api;
        _session = session;
        _streak = streak;
        _metrics = metrics;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateGreeting();
        UpdateLocalMetrics();
        await LoadStatsAsync();
    }

    private void UpdateGreeting()
    {
        greetingLabel.Text = DateTime.Now.ToString(
            "M월 d일 dddd", System.Globalization.CultureInfo.GetCultureInfo("ko-KR"));
    }

    private void UpdateLocalMetrics()
    {
        var today = _metrics.TodayReviews;
        var goal = _metrics.DailyGoal;

        goalProgress.Progress = _metrics.DailyGoalProgress;
        goalProgressLabel.Text = $"{Math.Min(today, goal)} / {goal}";
        goalMessageLabel.Text = today >= goal
            ? "일일 목표 완료"
            : today == 0
                ? "오늘의 학습"
                : $"목표까지 {goal - today}문제";
        primaryActionButton.Text = today >= goal ? "계속 학습" : "학습 시작";

        var level = _metrics.Level;
        headerXpLabel.Text = $"누적 {_metrics.TotalXp:N0} XP";
        levelStatLabel.Text = $"{level}";
        levelLabel.Text = $"레벨 {level}";
        levelXpLabel.Text = $"{_metrics.XpInLevel} / {_metrics.XpInLevel + _metrics.XpToNextLevel} XP";
        levelRemainLabel.Text = $"다음까지 {_metrics.XpToNextLevel} XP";
        levelProgress.Progress = _metrics.LevelProgress;
        streakLabel.Text = $"{_streak.Current}일";

        motivationLabel.Text = $"오늘 {today}문제 완료 · 일일 목표 {goal}문제";
    }

    private async Task LoadStatsAsync()
    {
        if (!_session.IsLoggedIn)
            return;

        loadingOverlay.IsVisible = true;
        errorBorder.IsVisible = false;

        try
        {
            var now = DateTime.UtcNow;
            var userId = _session.CurrentUserId!;

            var wordsTask = _api.GetWordsAsync();
            var wordProgressTask = _api.GetProgressAsync(userId);
            var grammarsTask = _api.GetGrammarAsync();
            var grammarProgressTask = _api.GetGrammarProgressAsync(userId);
            var idiomsTask = _api.GetIdiomAsync();
            var idiomProgressTask = _api.GetIdiomProgressAsync(userId);

            await Task.WhenAll(
                wordsTask,
                wordProgressTask,
                grammarsTask,
                grammarProgressTask,
                idiomsTask,
                idiomProgressTask);

            var words = await wordsTask;
            var wordProgress = await wordProgressTask;
            var grammars = await grammarsTask;
            var grammarProgress = await grammarProgressTask;
            var idioms = await idiomsTask;
            var idiomProgress = await idiomProgressTask;

            var wordDue = ReviewScheduler.CountDue(words, w => w.Id, ToProgressMap(wordProgress, p => p.WordId), now);
            var grammarDue = ReviewScheduler.CountDue(grammars, g => g.Id, ToProgressMap(grammarProgress, p => p.GrammarId), now);
            var idiomDue = ReviewScheduler.CountDue(idioms, i => i.Id, ToProgressMap(idiomProgress, p => p.IdiomId), now);
            var due = wordDue + grammarDue + idiomDue;
            var mastered = wordProgress.Count(p => p.Status == "Mastered")
                         + grammarProgress.Count(p => p.Status == "Mastered")
                         + idiomProgress.Count(p => p.Status == "Mastered");

            dueLabel.Text = $"{due}개";
            masteredLabel.Text = $"{mastered}개";
            wordDueLabel.Text = wordDue == 0
                ? "복습 대기 없음 · 문장 연습 가능"
                : $"복습 단어 {wordDue}개 · 문장 연습";
            grammarDueLabel.Text = DueMessage(grammarDue, "문법");
            idiomDueLabel.Text = DueMessage(idiomDue, "표현");
        }
        catch
        {
            errorLabel.Text = "학습 현황을 불러오지 못했습니다. 학습은 시작할 수 있습니다.";
            errorBorder.IsVisible = true;
        }
        finally
        {
            loadingOverlay.IsVisible = false;
        }
    }

    private static string DueMessage(int count, string label)
        => count == 0 ? "복습 대기 없음" : $"복습할 {label} {count}개";

    private static Dictionary<string, ILearningProgress> ToProgressMap<T>(
        IEnumerable<T> progress, Func<T, string> idOf) where T : ILearningProgress
        => progress
            .GroupBy(idOf)
            .ToDictionary(group => group.Key, group => (ILearningProgress)group.First());

    private async void OnStartWordReviewClick(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//learn");

    private async void OnStartGrammarReviewClick(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(TestGrammarView));

    private async void OnStartIdiomReviewClick(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(TestIdiomView));
}
