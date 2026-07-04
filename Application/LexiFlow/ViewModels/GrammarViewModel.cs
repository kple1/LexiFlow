using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LexiFlow.Models;
using LexiFlow.Services;
using System.Collections.ObjectModel;

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

    partial void OnSelectedCategoryChanged(string value)
    {
        FilterGrammars();
    }

    [RelayCommand]
    private async void LoadGrammars()
    {
        try
        {
            FilteredGrammars.Clear();
            Categories.Clear();
            Categories.Add("전체");

            _allGrammars = await _api.GetGrammarAsync();

            // Overlay this user's progress as a status badge on each grammar point.
            var statusByGrammar = await LoadProgressMapAsync();
            foreach (var g in _allGrammars)
                if (statusByGrammar.TryGetValue(g.Id, out var status))
                    g.UserStatus = status;

            var uniqueCategories = _allGrammars
                .Select(g => g.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            foreach (var category in uniqueCategories)
            {
                Categories.Add(category);
            }

            SelectedCategory = "전체";
            FilterGrammars();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", ex.Message, "Confirm");
        }
    }

    // grammarId -> status for the signed-in user; empty when logged out or server unavailable.
    private async Task<Dictionary<string, string>> LoadProgressMapAsync()
    {
        if (!_session.IsLoggedIn)
            return [];
        try
        {
            var progress = await _api.GetGrammarProgressAsync(_session.CurrentUserId!);
            return progress
                .GroupBy(p => p.GrammarId)
                .ToDictionary(g => g.Key, g => g.First().Status);
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
            : _allGrammars.Where(g => g.Category == SelectedCategory).ToList();

        foreach (var grammar in filtered)
        {
            FilteredGrammars.Add(grammar);
        }
    }

    [ObservableProperty]
    private Grammar? _selectedGrammar;

    [ObservableProperty]
    private bool _isDetailVisible;

    [RelayCommand]
    private void ShowGrammarDetail(Grammar grammar)
    {
        if (grammar == null)
            return;

        SelectedGrammar = grammar;
        IsDetailVisible = true;
    }

    [RelayCommand]
    private void CloseDetail() => IsDetailVisible = false;
}
