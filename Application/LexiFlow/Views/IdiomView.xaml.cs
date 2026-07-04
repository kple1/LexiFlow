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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadIdiomsCommand.Execute(null);
    }

    private async void OnQuizClick(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(TestIdiomView));
    }
}
