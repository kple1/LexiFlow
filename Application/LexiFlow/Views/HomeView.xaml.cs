using LexiFlow.Models;
using LexiFlow.Services;

namespace LexiFlow.Views;

public partial class HomeView : ContentPage
{
    private readonly ApiService _api;
    private readonly SessionService _session;
    private readonly StreakService _streak;
    private readonly NotificationService _notifications;

    public HomeView(ApiService api, SessionService session, StreakService streak, NotificationService notifications)
    {
        InitializeComponent();
        _api = api;
        _session = session;
        _streak = streak;
        _notifications = notifications;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        greetingLabel.Text = _session.IsLoggedIn ? $"Signed in as {_session.CurrentUserId}" : "";
        streakLabel.Text = _streak.Current.ToString();
        await LoadStatsAsync();
    }

    private async Task LoadStatsAsync()
    {
        if (!_session.IsLoggedIn)
            return;

        try
        {
            var now = DateTime.UtcNow;
            var userId = _session.CurrentUserId!;

            var words = await _api.GetWordsAsync();
            var wordProgress = await _api.GetProgressAsync(userId);
            var wordMap = ToProgressMap(wordProgress, p => p.WordId);

            var grammars = await _api.GetGrammarAsync();
            var grammarProgress = await _api.GetGrammarProgressAsync(userId);
            var grammarMap = ToProgressMap(grammarProgress, p => p.GrammarId);

            var due = ReviewScheduler.CountDue(words, w => w.Id, wordMap, now)
                    + ReviewScheduler.CountDue(grammars, g => g.Id, grammarMap, now);
            var mastered = wordProgress.Count(p => p.Status == "Mastered")
                         + grammarProgress.Count(p => p.Status == "Mastered");

            dueLabel.Text = due.ToString();
            masteredLabel.Text = mastered.ToString();

            // Nudge the user to come back and clear their due items.
            await _notifications.ScheduleDailyReminderAsync(due);
        }
        catch
        {
            // Server/progress endpoints may be unavailable; leave the current values.
        }
    }

    private static Dictionary<string, ILearningProgress> ToProgressMap<T>(
        IEnumerable<T> progress, Func<T, string> idOf) where T : ILearningProgress
        => progress
            .GroupBy(idOf)
            .ToDictionary(g => g.Key, g => (ILearningProgress)g.First());

    private async void OnStartWordReviewClick(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("//wordtest");

    private async void OnStartGrammarReviewClick(object sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(Views.TestGrammarView));
}
