using LexiFlow.Models;
using LexiFlow.Services;

namespace LexiFlow.Views;

public partial class TestGrammarView : ContentPage
{
    private readonly ApiService _api;
    private readonly SessionService _session;
    private readonly StreakService _streak;
    private List<Grammar> _cards = [];
    private int _index;

    public TestGrammarView(ApiService api, SessionService session, StreakService streak)
    {
        InitializeComponent();
        _api = api;
        _session = session;
        _streak = streak;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            var all = await _api.GetGrammarAsync();
            var progress = await LoadProgressMapAsync();

            // Spaced repetition over grammar points, same rules as words.
            var progressById = progress.ToDictionary(kv => kv.Key, kv => (ILearningProgress)kv.Value);
            _cards = ReviewScheduler.SelectForReview(all, g => g.Id, progressById, 10, DateTime.UtcNow);
            _index = 0;
            ShowCard();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.ToString(), "Confirm");
        }
    }

    private void ShowCard()
    {
        if (_index >= _cards.Count)
        {
            quizPanel.IsVisible = false;
            doneLabel.Text = _cards.Count == 0 ? "No grammar to review." : "Done!";
            doneLabel.IsVisible = true;
            return;
        }

        var g = _cards[_index];
        counter.Text = $"({_index + 1:D2}/{_cards.Count:D2})";
        titleLabel.Text = g.Title;
        categoryLabel.Text = g.Category;
        explanationLabel.Text = g.Explanation;
        exampleLabel.Text = g.Example;
        noteLabel.Text = g.Note;
        answerPanel.IsVisible = false;
        showButton.IsVisible = true;
    }

    private void OnShowClick(object sender, EventArgs e)
    {
        answerPanel.IsVisible = true;
        showButton.IsVisible = false;
    }

    // Self-graded: "Got it" masters the card, "Review again" keeps it in learning.
    private void OnGotItClick(object sender, EventArgs e) => Grade(correct: true, status: "Mastered");
    private void OnAgainClick(object sender, EventArgs e) => Grade(correct: false, status: "Learning");

    private void Grade(bool correct, string status)
    {
        if (_index < _cards.Count)
            RecordResult(_cards[_index], correct, status);
        _index++;
        ShowCard();
    }

    private async void RecordResult(Grammar grammar, bool correct, string status)
    {
        _streak.RegisterStudyToday();
        if (!_session.IsLoggedIn)
            return;
        try
        {
            await _api.UpsertGrammarProgressAsync(_session.CurrentUserId!, grammar.Id, correct, status);
        }
        catch
        {
            // Best-effort; ignore server/network errors.
        }
    }

    private async Task<Dictionary<string, GrammarProgress>> LoadProgressMapAsync()
    {
        if (!_session.IsLoggedIn)
            return [];
        try
        {
            var list = await _api.GetGrammarProgressAsync(_session.CurrentUserId!);
            return list
                .GroupBy(p => p.GrammarId)
                .ToDictionary(g => g.Key, g => g.First());
        }
        catch
        {
            return [];
        }
    }
}
