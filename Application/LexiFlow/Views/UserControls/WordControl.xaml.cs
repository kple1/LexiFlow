using LexiFlow.Models;

namespace LexiFlow.Views.UserControls;

public partial class WordControl : ContentView
{
	public WordControl()
	{
		InitializeComponent();
	}

    private async void OnWordsInformationClick(object sender, PointerEventArgs e)
    {
        var grid = sender as Grid;
        var data = grid.BindingContext as Word;
        if (Application.Current.MainPage is Page page)
        {
            await page.DisplayAlert(data.English, data.Example, "Confirm");
        }
    }
}