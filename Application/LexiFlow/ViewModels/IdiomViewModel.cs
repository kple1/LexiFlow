using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LexiFlow.Models;
using LexiFlow.Services;
using System.Collections.ObjectModel;

namespace LexiFlow.ViewModels;

public partial class IdiomViewModel : ObservableObject
{
    private readonly ApiService _api;
    private readonly SessionService _session;
    private List<Idiom> _allIdioms = [];

    public IdiomViewModel(ApiService api, SessionService session)
    {
        _api = api;
        _session = session;
    }

    public ObservableCollection<Idiom> FilteredIdioms { get; } = [];
    public ObservableCollection<string> Categories { get; } = ["전체"];

    [ObservableProperty]
    private string _selectedCategory = "전체";

    partial void OnSelectedCategoryChanged(string value)
    {
        FilterIdioms();
    }

    [RelayCommand]
    private async void LoadIdioms()
    {
        try
        {
            FilteredIdioms.Clear();
            Categories.Clear();
            Categories.Add("전체");

            _allIdioms = await _api.GetIdiomAsync();

            // Overlay this user's progress as a status badge on each idiom.
            var statusByIdiom = await LoadProgressMapAsync();
            foreach (var i in _allIdioms)
                if (statusByIdiom.TryGetValue(i.Id, out var status))
                    i.UserStatus = status;

            var uniqueCategories = _allIdioms
                .Select(i => i.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            foreach (var category in uniqueCategories)
            {
                Categories.Add(category);
            }

            SelectedCategory = "전체";
            FilterIdioms();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", ex.Message, "Confirm");
        }
    }

    // idiomId -> status for the signed-in user; empty when logged out or server unavailable.
    private async Task<Dictionary<string, string>> LoadProgressMapAsync()
    {
        if (!_session.IsLoggedIn)
            return [];
        try
        {
            var progress = await _api.GetIdiomProgressAsync(_session.CurrentUserId!);
            return progress
                .GroupBy(p => p.IdiomId)
                .ToDictionary(g => g.Key, g => g.First().Status);
        }
        catch
        {
            return [];
        }
    }

    private void FilterIdioms()
    {
        FilteredIdioms.Clear();

        var filtered = SelectedCategory == "전체"
            ? _allIdioms
            : _allIdioms.Where(i => i.Category == SelectedCategory).ToList();

        foreach (var idiom in filtered)
        {
            FilteredIdioms.Add(idiom);
        }
    }

    [ObservableProperty]
    private Idiom? _selectedIdiom;

    [ObservableProperty]
    private bool _isDetailVisible;

    [RelayCommand]
    private void ShowIdiomDetail(Idiom idiom)
    {
        if (idiom == null)
            return;

        SelectedIdiom = idiom;
        IsDetailVisible = true;
    }

    [RelayCommand]
    private void CloseDetail() => IsDetailVisible = false;
}
