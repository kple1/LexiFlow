using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LexiFlow.Models;
using LexiFlow.Services;

namespace LexiFlow.ViewModels;

public partial class WordsViewModel : ObservableObject
{
    private readonly ApiService _api;
    private readonly SessionService _session;

    public WordsViewModel(ApiService api, SessionService session)
    {
        _api = api;
        _session = session;
    }

    public ObservableCollection<Word> Words { get; } = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = "";

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [ObservableProperty]
    private string _resultSummary = "단어를 불러오는 중이에요";

    [RelayCommand]
    private async Task LoadWordsAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        ErrorMessage = "";

        try
        {
            var wordsTask = _api.GetWordsAsync();
            var progressTask = LoadProgressMapAsync();
            await Task.WhenAll(wordsTask, progressTask);

            var result = await wordsTask;
            var query = SearchText.Trim();
            if (query.Length > 0)
            {
                result = result
                    .Where(word =>
                        word.English.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        word.Meaning.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var statusByWord = await progressTask;
            Words.Clear();
            foreach (var word in result)
            {
                word.UserStatus = statusByWord.GetValueOrDefault(word.Id);
                Words.Add(word);
            }

            ResultSummary = query.Length == 0
                ? $"전체 {Words.Count}개 단어"
                : $"‘{query}’ 검색 결과 {Words.Count}개";
        }
        catch
        {
            Words.Clear();
            ResultSummary = "단어를 불러오지 못했어요";
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
            var progress = await _api.GetProgressAsync(_session.CurrentUserId!);
            return progress
                .GroupBy(item => item.WordId)
                .ToDictionary(group => group.Key, group => group.First().Status);
        }
        catch
        {
            return [];
        }
    }
}
