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

    private async void OnSubmitClick(object sender, EventArgs e) => await CheckAnswerAsync();

    // Pressing Enter in the answer box submits — same as the Submit button.
    // (Previously this just skipped to the next word without checking the answer.)
    private async void OnEntryCompleted(object sender, EventArgs e) => await CheckAnswerAsync();

    // Grade the typed answer and tell the user whether it's right or wrong.
    private async Task CheckAnswerAsync()
    {
        var state = _words[_page];
        var input = (answer.Text ?? "").Trim();
        var expected = (state.Word.Meaning ?? "").Trim();

        if (input.Length == 0)
            return; // ignore empty submissions

        if (input == expected)
        {
            wrong.IsVisible = false;
            state.IsCorrect = true;
            // Mastered only if they never missed this word; otherwise it's still being learned.
            RecordResult(state, correct: true, status: state.Missed ? "Learning" : "Mastered");
            await DisplayAlert("정답", "정답입니다! 🎉", "다음");
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
            await DisplayAlert("오답", "틀렸습니다. 다시 시도해 보세요.", "확인");
            answer.Focus();
        }
    }

    private async void OnCheckClick(object sender, EventArgs e)
    {
        answer.Focus();
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

    // Explicit skip control — moves on without grading.
    private void OnNextClick(object sender, EventArgs e)
    {
        answer.Focus();
        NextPage();
    }

	private void NextPage()
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
}
