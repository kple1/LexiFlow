using LexiFlow.Models;
using LexiFlow.Services;

namespace LexiFlow.Views;

public partial class TestWordsView : ContentPage
{
	private List<CorrectWordState> _words = [];
	private int _page = 0;
	private readonly ApiService _api;
	public TestWordsView(ApiService api)
	{
		InitializeComponent();
		_api = api;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		try
		{
            var allWords = await _api.GetWordsAsync();

			_words = [.. allWords
				.OrderBy(_ => Random.Shared.Next())
				.Take(10)
				.Select(word => new CorrectWordState { Word = word, IsCorrect = false })];

            display.Text = $"{_words[_page].Word.English}\n(01/10)";
        }
        catch (Exception ex)
		{
			await DisplayAlert("Error", ex.ToString(), "Confirm");
		}
	}

    private async void OnSubmitClick(object sender, EventArgs e)
    {
		if (_words[_page].Word.Meaning == answer.Text)
		{
			wrong.IsVisible = false;
			_words[_page].IsCorrect = true;
			NextPage();
		}
		else
		{
            wrong.IsVisible = true;
        }
    }

    private async void OnCheckClick(object sender, EventArgs e)
    {
		await DisplayAlert("", _words[_page].Word.Meaning, "Confirm");
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
        display.Text = $"{_words[_page].Word.English}\n({_page + 1:N0}/10)";
        answer.Text = "";
    }

    private void OnEntryCompleted(object sender, EventArgs e)
    {
		NextPage();
    }
}