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
        var timeGreeting = DateTime.Now.Hour switch
        {
            < 6 => "늦은 시간에도 멋진 집중이에요",
            < 12 => "좋은 아침이에요",
            < 18 => "좋은 오후예요",
            _ => "오늘 하루도 수고했어요"
        };

        greetingLabel.Text = timeGreeting;
    }

    private void UpdateLocalMetrics()
    {
        var today = _metrics.TodayReviews;
        var goal = _metrics.DailyGoal;

        goalProgress.Progress = _metrics.DailyGoalProgress;
        goalProgressLabel.Text = $"{Math.Min(today, goal)} / {goal}";
        goalMessageLabel.Text = today >= goal
            ? "오늘의 목표를 달성했어요!"
            : today == 0
                ? "첫 문제를 풀고 흐름을 만들어 보세요"
                : $"목표까지 {goal - today}문제 남았어요";
        primaryActionButton.Text = today >= goal ? "문장으로 기억 더 단단하게" : "오늘의 문장 학습";

        var level = _metrics.Level;
        headerXpLabel.Text = $"{_metrics.TotalXp:N0} XP";
        levelStatLabel.Text = $"LEVEL {level}";
        levelLabel.Text = $"레벨 {level} 진행도";
        levelXpLabel.Text = $"{_metrics.XpInLevel} / {_metrics.XpInLevel + _metrics.XpToNextLevel} XP";
        levelRemainLabel.Text = $"다음 레벨까지 {_metrics.XpToNextLevel} XP";
        levelProgress.Progress = _metrics.LevelProgress;
        streakLabel.Text = $"{_streak.Current}일";

        motivationLabel.Text = today >= goal
            ? "목표 달성! 오늘 쌓은 기억을 한 번 더 강화해도 좋아요."
            : _streak.Current > 1
                ? $"{_streak.Current}일째 이어지는 흐름, 오늘도 가볍게 연결해요."
                : "짧게 해도 괜찮아요. 꾸준함이 실력을 만듭니다.";
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
                ? "뜻 고르기와 빈칸 쓰기 · 10문장"
                : $"복습할 핵심 단어 {wordDue}개를 문장으로 연습";
            grammarDueLabel.Text = DueMessage(grammarDue, "문법");
            idiomDueLabel.Text = DueMessage(idiomDue, "표현");
        }
        catch
        {
            errorLabel.Text = "학습 현황을 불러오지 못했어요. 복습은 그대로 시작할 수 있어요.";
            errorBorder.IsVisible = true;
        }
        finally
        {
            loadingOverlay.IsVisible = false;
        }
    }

    private static string DueMessage(int count, string label)
        => count == 0 ? $"오늘 예정된 {label} 복습을 마쳤어요" : $"지금 복습하면 좋은 {label} {count}개";

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
