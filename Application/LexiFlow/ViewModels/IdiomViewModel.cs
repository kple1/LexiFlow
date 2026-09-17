using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LexiFlow.Models;
using LexiFlow.Services;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = "";

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [ObservableProperty]
    private string _resultSummary = "표현 카드를 불러오는 중이에요";

    partial void OnSelectedCategoryChanged(string value) => FilterIdioms();

    [RelayCommand]
    private async Task LoadIdiomsAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        ErrorMessage = "";

        try
        {
            var idiomTask = _api.GetIdiomAsync();
            var progressTask = LoadProgressMapAsync();
            await Task.WhenAll(idiomTask, progressTask);

            _allIdioms = await idiomTask;
            var statusByIdiom = await progressTask;
            foreach (var idiom in _allIdioms)
                idiom.UserStatus = statusByIdiom.GetValueOrDefault(idiom.Id);

            Categories.Clear();
            Categories.Add("전체");
            foreach (var category in _allIdioms
                         .Select(idiom => idiom.Category)
                         .Where(category => !string.IsNullOrWhiteSpace(category))
                         .Distinct()
                         .OrderBy(category => category))
            {
                Categories.Add(category);
            }

            SelectedCategory = "전체";
            FilterIdioms();
        }
        catch
        {
            _allIdioms = [];
            FilteredIdioms.Clear();
            ResultSummary = "표현 카드를 불러오지 못했어요";
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
            var progress = await _api.GetIdiomProgressAsync(_session.CurrentUserId!);
            return progress
                .GroupBy(item => item.IdiomId)
                .ToDictionary(group => group.Key, group => group.First().Status);
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
            : _allIdioms.Where(idiom => idiom.Category == SelectedCategory).ToList();

        foreach (var idiom in filtered)
            FilteredIdioms.Add(idiom);

        ResultSummary = SelectedCategory == "전체"
            ? $"전체 {FilteredIdioms.Count}개 표현"
            : $"{SelectedCategory} · {FilteredIdioms.Count}개";
    }

    [ObservableProperty]
    private Idiom? _selectedIdiom;

    [ObservableProperty]
    private bool _isDetailVisible;

    [RelayCommand]
    private void ShowIdiomDetail(Idiom? idiom)
    {
        if (idiom is null)
            return;

        SelectedIdiom = idiom;
        IsDetailVisible = true;
    }

    [RelayCommand]
    private void CloseDetail() => IsDetailVisible = false;
}
