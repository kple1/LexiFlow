using LexiFlow.Models;
using LexiFlow.Services;

namespace LexiFlow.Views;

public partial class TestIdiomView : ContentPage
{
    private readonly ApiService _api;
    private readonly SessionService _session;
    private readonly StreakService _streak;
    private List<Idiom> _cards = [];
    private int _index;

    public TestIdiomView(ApiService api, SessionService session, StreakService streak)
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
            var all = await _api.GetIdiomAsync();
            var progress = await LoadProgressMapAsync();

            // Spaced repetition over idioms, same rules as words/grammar.
            var progressById = progress.ToDictionary(kv => kv.Key, kv => (ILearningProgress)kv.Value);
            _cards = ReviewScheduler.SelectForReview(all, i => i.Id, progressById, 10, DateTime.UtcNow);
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
            doneLabel.Text = _cards.Count == 0 ? "No idioms to review." : "Done!";
            doneLabel.IsVisible = true;
            return;
        }

        var i = _cards[_index];
        counter.Text = $"({_index + 1:D2}/{_cards.Count:D2})";
        titleLabel.Text = i.Title;
        categoryLabel.Text = i.Category;
        explanationLabel.Text = i.Explanation;
        exampleLabel.Text = i.Example;
        noteLabel.Text = i.Note;
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

    private async void RecordResult(Idiom idiom, bool correct, string status)
    {
        _streak.RegisterStudyToday();
        if (!_session.IsLoggedIn)
            return;
        try
        {
            await _api.UpsertIdiomProgressAsync(_session.CurrentUserId!, idiom.Id, correct, status);
        }
        catch
        {
            // Best-effort; ignore server/network errors.
        }
    }

    private async Task<Dictionary<string, IdiomProgress>> LoadProgressMapAsync()
    {
        if (!_session.IsLoggedIn)
            return [];
        try
        {
            var list = await _api.GetIdiomProgressAsync(_session.CurrentUserId!);
            return list
                .GroupBy(p => p.IdiomId)
                .ToDictionary(g => g.Key, g => g.First());
        }
        catch
        {
            return [];
        }
    }
}
