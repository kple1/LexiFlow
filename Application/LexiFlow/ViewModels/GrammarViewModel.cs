using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LexiFlow.Models;
using LexiFlow.Services;
using System.Collections.ObjectModel;

namespace LexiFlow.ViewModels;

public partial class GrammarViewModel : ObservableObject
{
    private readonly ApiService _api;
    private List<Grammar> _allGrammars = [];

    public GrammarViewModel(ApiService api)
    {
        _api = api;
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
