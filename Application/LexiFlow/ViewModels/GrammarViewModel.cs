using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LexiFlow.Models;
using LexiFlow.Services;

namespace LexiFlow.ViewModels;

public partial class GrammarViewModel : ObservableObject
{
    private readonly ApiService _api;
    private readonly SessionService _session;
    private List<Grammar> _allGrammars = [];

    public GrammarViewModel(ApiService api, SessionService session)
    {
        _api = api;
        _session = session;
    }

    public ObservableCollection<Grammar> FilteredGrammars { get; } = [];
    public ObservableCollection<string> Categories { get; } = ["전체"];

    [ObservableProperty]
    private string _selectedCategory = "전체";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = "";

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [ObservableProperty]
    private string _resultSummary = "문법 카드를 불러오는 중이에요";

    partial void OnSelectedCategoryChanged(string value) => FilterGrammars();

    [RelayCommand]
    private async Task LoadGrammarsAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        ErrorMessage = "";

        try
        {
            var grammarTask = _api.GetGrammarAsync();
            var progressTask = LoadProgressMapAsync();
            await Task.WhenAll(grammarTask, progressTask);

            _allGrammars = await grammarTask;
            var statusByGrammar = await progressTask;
            foreach (var grammar in _allGrammars)
                grammar.UserStatus = statusByGrammar.GetValueOrDefault(grammar.Id);

            Categories.Clear();
            Categories.Add("전체");
            foreach (var category in _allGrammars
                         .Select(grammar => grammar.Category)
                         .Where(category => !string.IsNullOrWhiteSpace(category))
                         .Distinct()
                         .OrderBy(category => category))
            {
                Categories.Add(category);
            }

            SelectedCategory = "전체";
            FilterGrammars();
        }
        catch
        {
            _allGrammars = [];
            FilteredGrammars.Clear();
            ResultSummary = "문법 카드를 불러오지 못했어요";
            ErrorMessage = "네트워크를 확인한 뒤 다시 시도해 주세요.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<Dictionary<string, string>> LoadProgressMapAsync()
    {
        if (!_session.IsLoggedIn)
            return [];

        try
        {
            var progress = await _api.GetGrammarProgressAsync(_session.CurrentUserId!);
            return progress
                .GroupBy(item => item.GrammarId)
                .ToDictionary(group => group.Key, group => group.First().Status);
        }
        catch
        {
            return [];
        }
    }

    private void FilterGrammars()
    {
        FilteredGrammars.Clear();
        var filtered = SelectedCategory == "전체"
            ? _allGrammars
            : _allGrammars.Where(grammar => grammar.Category == SelectedCategory).ToList();

        foreach (var grammar in filtered)
            FilteredGrammars.Add(grammar);

        ResultSummary = SelectedCategory == "전체"
            ? $"전체 {FilteredGrammars.Count}개 문법"
            : $"{SelectedCategory} · {FilteredGrammars.Count}개";
    }

    [ObservableProperty]
    private Grammar? _selectedGrammar;

    [ObservableProperty]
    private bool _isDetailVisible;

    [RelayCommand]
    private void ShowGrammarDetail(Grammar? grammar)
    {
        if (grammar is null)
            return;

        SelectedGrammar = grammar;
        IsDetailVisible = true;
    }

    [RelayCommand]
    private void CloseDetail() => IsDetailVisible = false;
}
