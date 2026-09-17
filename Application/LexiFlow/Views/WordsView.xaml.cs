using LexiFlow.Models;
using LexiFlow.ViewModels;

namespace LexiFlow.Views;

public partial class WordsView : ContentPage
{
    private readonly WordsViewModel _viewModel;

    public WordsView(WordsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_viewModel.Words.Count == 0)
            await _viewModel.LoadWordsCommand.ExecuteAsync(null);
    }

    private async void OnSearchCompleted(object? sender, EventArgs e)
        => await _viewModel.LoadWordsCommand.ExecuteAsync(null);

    private async void OnWordTapped(object? sender, TappedEventArgs e)
    {
        if (sender is TapGestureRecognizer { BindingContext: Word word })
            await Navigation.PushAsync(new WordDetailView(word));
    }

    private async void OnStartReviewClick(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//learn");
}
