using LexiFlow.Models;
using LexiFlow.Services;

namespace LexiFlow.Views;

public partial class TestWordsView : ContentPage
{
	private List<CorrectWordState> _words = [];
	private int _page = 0;
	private readonly ApiService _api;
	private readonly SessionService _session;
	private readonly StreakService _streak;
	public TestWordsView(ApiService api, SessionService session, StreakService streak)
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
            var allWords = await _api.GetWordsAsync();
            var progress = await LoadProgressMapAsync();

            // Spaced repetition: prefer words that are due for review over a plain shuffle.
            var progressById = progress.ToDictionary(kv => kv.Key, kv => (ILearningProgress)kv.Value);
            var selected = ReviewScheduler.SelectForReview(allWords, w => w.Id, progressById, 10, DateTime.UtcNow);
            _page = 0;
            _words = [.. selected.Select(word => new CorrectWordState { Word = word, IsCorrect = false })];

            if (_words.Count == 0)
            {
                display.Text = "No words to review.";
                return;
            }

            display.Text = $"{_words[_page].Word.English}\n{Counter()}";
        }
        catch (Exception ex)
		{
			await DisplayAlert("Error", ex.ToString(), "Confirm");
		}
	}

    // wordId -> progress for the signed-in user; empty when logged out or the server is unreachable.
    private async Task<Dictionary<string, WordProgress>> LoadProgressMapAsync()
    {
        if (!_session.IsLoggedIn)
            return [];
        try
        {
            var list = await _api.GetProgressAsync(_session.CurrentUserId!);
            return list
                .GroupBy(p => p.WordId)
                .ToDictionary(g => g.Key, g => g.First());
        }
        catch
        {
            return [];
        }
    }

    private string Counter() => $"({_page + 1:D2}/{_words.Count:D2})";

    private async void OnSubmitClick(object sender, EventArgs e)
    {
		var state = _words[_page];
		if (state.Word.Meaning == answer.Text)
		{
			wrong.IsVisible = false;
			state.IsCorrect = true;
			// Mastered only if they never missed this word; otherwise it's still being learned.
			RecordResult(state, correct: true, status: state.Missed ? "Learning" : "Mastered");
			NextPage();
		}
		else
		{
            wrong.IsVisible = true;
			// Record a single "wrong" result the first time they miss this word.
			if (!state.Missed)
			{
				state.Missed = true;
				RecordResult(state, correct: false, status: "Learning");
			}
        }
    }

    private async void OnCheckClick(object sender, EventArgs e)
    {
		var state = _words[_page];
		// Revealing the answer counts as a miss, so it can't be Mastered on this round.
		if (!state.Missed)
		{
			state.Missed = true;
			RecordResult(state, correct: false, status: "Learning");
		}
		await DisplayAlert("", state.Word.Meaning, "Confirm");
    }

    // Fire-and-forget: don't block the quiz, and tolerate the server being offline
    // (e.g. the progress endpoint isn't deployed yet).
    private async void RecordResult(CorrectWordState state, bool correct, string status)
    {
		_streak.RegisterStudyToday();
		if (!_session.IsLoggedIn)
			return;
		try
		{
			await _api.UpsertProgressAsync(_session.CurrentUserId!, state.Word.Id, correct, status);
		}
		catch
		{
			// Ignore network/server errors; progress is best-effort.
		}
    }

    private async void OnNextClick(object sender, EventArgs e)
    {
		NextPage();
    }

	private async void NextPage()
	{
        wrong.IsVisible = false;

        _page += 1;
        if (_page >= _words.Count)
        {
            if (_words.Any(x => !x.IsCorrect))
			{
				_page = _words.FindIndex(x => !x.IsCorrect);
            }
			else
			{
				display.Text = "Congratulations!";
				results.ItemsSource = _words.Select(x => x.Word).ToList();
				return;
			}
        }
        display.Text = $"{_words[_page].Word.English}\n{Counter()}";
        answer.Text = "";
    }

    private void OnEntryCompleted(object sender, EventArgs e)
    {
		NextPage();
    }
}