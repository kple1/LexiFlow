using LexiFlow.ViewModels;

namespace LexiFlow.Views;

public partial class IdiomView : ContentPage
{
    private readonly IdiomViewModel _viewModel;

    public IdiomView(IdiomViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_viewModel.FilteredIdioms.Count == 0)
            await _viewModel.LoadIdiomsCommand.ExecuteAsync(null);
    }

    private async void OnQuizClick(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(TestIdiomView));
}
