using LexiFlow.Models;
using LexiFlow.Services;
using LexiFlow.ViewModels;
using LexiFlow.Views.UserControls;

namespace LexiFlow.Views;

public partial class WordsView : ContentPage
{
    private WordsViewModel _vm;
    public WordsView(WordsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_vm.LoadWordsCommand.CanExecute(null))
            _vm.LoadWordsCommand.Execute(null);
    }

    private void OnSearchClick(object sender, PointerEventArgs e)
    {
        if (_vm.LoadWordsCommand.CanExecute(null))
            _vm.LoadWordsCommand.Execute(null);
    }

    private void OnWordClick(object sender, PointerEventArgs e)
    {
        var control = sender as WordControl;
        var word = control.BindingContext as Word;
        var example = word.Example;

        if (_vm.WordInformationCommand.CanExecute(example))
            _vm.WordInformationCommand.Execute(example);
    }
}