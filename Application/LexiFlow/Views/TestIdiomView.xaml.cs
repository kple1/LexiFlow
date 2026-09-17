using LexiFlow.Models;
using LexiFlow.Services;

namespace LexiFlow.Views;

public partial class TestIdiomView : ContentPage
{
    private readonly ApiService _api;
    private readonly SessionService _session;
    private readonly StreakService _streak;
    private readonly LearningMetricsService _metrics;
    private readonly Queue<Idiom> _remaining = new();
    private readonly HashSet<string> _completed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    private List<Idiom> _cards = [];
    private int _firstPassCorrect;
    private int _sessionXp;
    private bool _isLoading;

    public TestIdiomView(
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
        if (_cards.Count == 0 && !_isLoading)
            await LoadCardsAsync();
    }

    private async Task LoadCardsAsync()
    {
        _isLoading = true;
        loadingOverlay.IsVisible = true;
        quizPanel.IsVisible = false;
        donePanel.IsVisible = false;

        try
        {
            var all = await _api.GetIdiomAsync();
            var progress = await LoadProgressMapAsync();
            var progressById = progress.ToDictionary(pair => pair.Key, pair => (ILearningProgress)pair.Value);
            _cards = ReviewScheduler.SelectForReview(all, item => item.Id, progressById, 10, DateTime.UtcNow);
            RestartSession();
        }
        catch
        {
            _cards = [];
            ShowDone("표현 카드를 불러오지 못했어요. 잠시 후 다시 시도해 주세요.");
        }
        finally
        {
            _isLoading = false;
            loadingOverlay.IsVisible = false;
        }
    }

    private void RestartSession()
    {
        _remaining.Clear();
        foreach (var card in _cards)
            _remaining.Enqueue(card);

        _completed.Clear();
        _seen.Clear();
        _firstPassCorrect = 0;
        _sessionXp = 0;
        donePanel.IsVisible = false;

        if (_cards.Count == 0)
        {
            ShowDone("지금 복습할 표현 카드가 없어요.");
            return;
        }

        quizPanel.IsVisible = true;
        ShowCard();
    }

    private void ShowCard()
    {
        if (_remaining.Count == 0)
        {
            ShowDone("헷갈린 표현까지 모두 다시 확인했어요.");
            return;
        }

        var idiom = _remaining.Peek();
        titleLabel.Text = idiom.Title;
        categoryLabel.Text = string.IsNullOrWhiteSpace(idiom.Category) ? "EXPRESSION" : idiom.Category.ToUpperInvariant();
        explanationLabel.Text = idiom.Explanation;
        exampleLabel.Text = idiom.Example;
        noteLabel.Text = idiom.Note;
        noteBorder.IsVisible = !string.IsNullOrWhiteSpace(idiom.Note);
        answerPanel.IsVisible = false;
        showButton.IsVisible = true;
        sessionProgress.Progress = (double)_completed.Count / _cards.Count;
        counterLabel.Text = $"{Math.Min(_completed.Count + 1, _cards.Count)} / {_cards.Count}";
        sessionXpLabel.Text = $"+{_sessionXp} XP";
    }

    private void OnShowClick(object? sender, EventArgs e)
    {
        answerPanel.IsVisible = true;
        showButton.IsVisible = false;
    }

    private async void OnGotItClick(object? sender, EventArgs e)
        => await GradeAsync(correct: true, "Mastered");

    private async void OnAgainClick(object? sender, EventArgs e)
        => await GradeAsync(correct: false, "Learning");

    private async Task GradeAsync(bool correct, string status)
    {
        if (_remaining.Count == 0)
            return;

        var card = _remaining.Dequeue();
        var firstEncounter = _seen.Add(card.Id);
        if (correct)
        {
            _completed.Add(card.Id);
            if (firstEncounter)
                _firstPassCorrect++;
        }
        else
        {
            _remaining.Enqueue(card);
        }

        _streak.RegisterStudyToday();
        _sessionXp += _metrics.RegisterReview(correct);

        if (_session.IsLoggedIn)
        {
            try
            {
                await _api.UpsertIdiomProgressAsync(_session.CurrentUserId!, card.Id, correct, status);
            }
            catch
            {
                // Keep the local learning flow responsive when the server is unavailable.
            }
        }

        ShowCard();
    }

    private void ShowDone(string message)
    {
        quizPanel.IsVisible = false;
        donePanel.IsVisible = true;
        doneMessageLabel.Text = message;
        scoreLabel.Text = $"{_firstPassCorrect} / {_cards.Count}";
        earnedXpLabel.Text = $"+{_sessionXp} XP";
    }

    private void OnRestartClick(object? sender, EventArgs e) => RestartSession();

    private async void OnHomeClick(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//home");

    private async Task<Dictionary<string, IdiomProgress>> LoadProgressMapAsync()
    {
        if (!_session.IsLoggedIn)
            return [];

        try
        {
            var list = await _api.GetIdiomProgressAsync(_session.CurrentUserId!);
            return list
                .GroupBy(progress => progress.IdiomId)
                .ToDictionary(group => group.Key, group => group.First());
        }
        catch
        {
            return [];
        }
    }
}
