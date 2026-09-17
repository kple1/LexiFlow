using LexiFlow.ViewModels;

namespace LexiFlow.Views;

public partial class GrammarView : ContentPage
{
    private readonly GrammarViewModel _viewModel;

    public GrammarView(GrammarViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_viewModel.FilteredGrammars.Count == 0)
            await _viewModel.LoadGrammarsCommand.ExecuteAsync(null);
    }

    private async void OnQuizClick(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(TestGrammarView));
}
