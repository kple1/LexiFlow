using LexiFlow.Services;
using LexiFlow.ViewModels;

namespace LexiFlow.Views;

public partial class GrammarView : ContentPage
{
    private readonly GrammarViewModel _viewModel;

    public GrammarView(ApiService api)
    {
        InitializeComponent();
        _viewModel = new GrammarViewModel(api);
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadGrammarsCommand.Execute(null);
    }
}