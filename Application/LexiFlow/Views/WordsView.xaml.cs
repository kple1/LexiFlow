using LexiFlow.Models;
using LexiFlow.Services;
using LexiFlow.ViewModels;
using System.Globalization;

namespace LexiFlow.Views;

public partial class WordsView : ContentPage
{
    private readonly WordsViewModel _viewModel;
    private readonly ArchiveService _archive;
    private readonly SessionService _session;
    private bool _openingWord;
    private bool? _wideLibraryLayout;

    public WordsView(WordsViewModel viewModel, ArchiveService archive, SessionService session)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _archive = archive;
        _session = session;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _session.StateChanged -= OnSessionChanged;
        _session.StateChanged += OnSessionChanged;
        // Refresh progress after a lesson/detail visit, including when switching accounts.
        await _viewModel.LoadWordsCommand.ExecuteAsync(null);
    }

    protected override void OnDisappearing()
    {
        _session.StateChanged -= OnSessionChanged;
        base.OnDisappearing();
    }

    private void OnSessionChanged(object? sender, EventArgs e)
        => MainThread.BeginInvokeOnMainThread(async () =>
        {
            _viewModel.InvalidateAccountData();
            if (!_viewModel.IsBusy) await _viewModel.LoadWordsCommand.ExecuteAsync(null);
        });

    private void OnSearchCompleted(object? sender, EventArgs e) => searchEntry.Unfocus();

    private void OnLibrarySizeChanged(object? sender, EventArgs e)
    {
        var wide = wordList.Width >= 850;
        if (_wideLibraryLayout == wide) return;
        _wideLibraryLayout = wide;
        wordLayout.Span = wide ? 2 : 1;
        filterGrid.ColumnDefinitions.Clear();
        filterGrid.RowDefinitions.Clear();
        for (var index = 0; index < (wide ? 4 : 2); index++)
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var index = 0; index < (wide ? 1 : 2); index++)
            filterGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        View[] filters = [levelFilter, topicFilter, statusFilter, sortFilter];
        for (var index = 0; index < filters.Length; index++)
        {
            Grid.SetRow(filters[index], wide ? 0 : index / 2);
            Grid.SetColumn(filters[index], wide ? index : index % 2);
        }
    }

    private async void OnWordTapped(object? sender, TappedEventArgs e) => await OpenWordAsync(sender);
    private async void OnWordOpenClick(object? sender, EventArgs e) => await OpenWordAsync(sender);

    private async Task OpenWordAsync(object? sender)
    {
        if (_openingWord || _viewModel.IsBusy) return;
        var word = (sender as BindableObject)?.BindingContext as Word;
        if (word is null) return;
        _openingWord = true;
        try { await Navigation.PushAsync(new WordDetailView(word, _archive, _session)); }
        finally { _openingWord = false; }
    }

    private async void OnStartReviewClick(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//learn");
}

public sealed class WordStatusLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => WordLibraryQuery.StatusLabel(value as string);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
