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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadGrammarsCommand.Execute(null);
    }

    private async void OnQuizClick(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(TestGrammarView));
    }
}
