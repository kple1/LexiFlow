using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LexiFlow.Models;
using LexiFlow.Services;

namespace LexiFlow.ViewModels;

public partial class WordsViewModel : ObservableObject
{
    private readonly ApiService _api;
    private readonly SessionService _session;
    private List<Word> _allWords = [];
    private bool _progressAvailable;
    private bool _resettingFilters;
    private int _accountGeneration;

    public WordsViewModel(ApiService api, SessionService session)
    {
        _api = api;
        _session = session;
    }

    // Replace one immutable result snapshot instead of sending hundreds of collection events per keystroke.
    [ObservableProperty] private IReadOnlyList<Word> _words = [];
    public IReadOnlyList<string> LevelOptions { get; } = [WordLibraryQuery.AllLevels, "기초", "일상", "확장", "미분류"];
    public IReadOnlyList<string> StatusOptions { get; } = [WordLibraryQuery.AllStatuses, "새 단어", "학습 중", "익힌 단어", "미확인"];
    public IReadOnlyList<string> SortOptions { get; } = [WordLibraryQuery.Alphabetical, WordLibraryQuery.ReverseAlphabetical];

    [ObservableProperty] private IReadOnlyList<string> _topicOptions = [WordLibraryQuery.AllTopics];
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _selectedLevel = WordLibraryQuery.AllLevels;
    [ObservableProperty] private string _selectedTopic = WordLibraryQuery.AllTopics;
    [ObservableProperty] private string _selectedStatus = WordLibraryQuery.AllStatuses;
    [ObservableProperty] private string _selectedSort = WordLibraryQuery.Alphabetical;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;
    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = "";
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string _noticeMessage = "";
    public bool HasNotice => !string.IsNullOrWhiteSpace(NoticeMessage);

    [ObservableProperty] private string _resultSummary = "단어를 불러오는 중이에요";
    [ObservableProperty] private string _progressSummary = "로그인하면 내 학습 기록을 확인할 수 있어요.";
    [ObservableProperty] private string _emptyTitle = "조건에 맞는 단어가 없어요";
    [ObservableProperty] private string _emptyDescription = "검색어를 줄이거나 필터를 초기화해 보세요.";

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnSelectedLevelChanged(string value) => ApplyFilters();
    partial void OnSelectedTopicChanged(string value) => ApplyFilters();
    partial void OnSelectedStatusChanged(string value) => ApplyFilters();
    partial void OnSelectedSortChanged(string value) => ApplyFilters();

    public void InvalidateAccountData()
    {
        _accountGeneration++;
        _progressAvailable = false;
        foreach (var word in _allWords) word.UserStatus = "Unknown";
        ProgressSummary = "학습 기록을 다시 확인하고 있어요.";
        ApplyFilters();
    }

    [RelayCommand]
    private void ResetFilters()
    {
        _resettingFilters = true;
        SearchText = "";
        SelectedLevel = WordLibraryQuery.AllLevels;
        SelectedTopic = WordLibraryQuery.AllTopics;
        SelectedStatus = WordLibraryQuery.AllStatuses;
        SelectedSort = WordLibraryQuery.Alphabetical;
        _resettingFilters = false;
        ApplyFilters();
    }

    [RelayCommand]
    private async Task LoadWordsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = "";
        NoticeMessage = "";
        InvalidateAccountData();
        try
        {
            // Never apply an in-flight response to a changed account or login token.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var generation = _accountGeneration;
                var token = _session.AccessToken;
                var account = _session.StorageId;
                var userId = _session.IsLoggedIn ? _session.CurrentUserId : null;
                var wordsTask = _api.GetWordsAsync();
                var progressTask = LoadProgressAsync(userId);
                await Task.WhenAll(wordsTask, progressTask);
                var progress = await progressTask;
                if (generation != _accountGeneration || token != _session.AccessToken || account != _session.StorageId)
                {
                    if (progress.AuthenticationFailed)
                        NoticeMessage = "로그인이 만료되어 학습 기록을 표시하지 않았어요. 다시 로그인해 주세요.";
                    continue;
                }

                _allWords = await wordsTask;
                _progressAvailable = progress.Available;
                foreach (var word in _allWords)
                    word.UserStatus = _progressAvailable || (progress.LocalAvailable && BuiltInWordProgressService.IsBuiltInId(word.Id))
                        ? progress.Statuses.GetValueOrDefault(word.Id, "New") : "Unknown";

                TopicOptions = WordLibraryQuery.TopicOptions(_allWords);
                if (!TopicOptions.Contains(SelectedTopic)) SelectedTopic = WordLibraryQuery.AllTopics;
                var notices = new List<string>();
                if (!string.IsNullOrWhiteSpace(NoticeMessage)) notices.Add(NoticeMessage);
                if (_api.LastWordLoadUsedFallback)
                    notices.Add("서버에 연결하지 못해 내장 단어팩을 표시하고 있어요. 최신 서버 단어는 새로고침 후 확인해 주세요.");
                if (!string.IsNullOrWhiteSpace(progress.Message)) notices.Add(progress.Message);
                NoticeMessage = string.Join("\n", notices);
                ProgressSummary = _progressAvailable
                    ? $"익힌 단어 {_allWords.Count(word => word.UserStatus == "Mastered")}개 · 학습 중 {_allWords.Count(word => word.UserStatus == "Learning")}개 · 새 단어 {_allWords.Count(word => WordLibraryQuery.StatusLabel(word.UserStatus) == "새 단어")}개"
                    : progress.LocalAvailable
                        ? $"이 기기 기본팩: 익힌 단어 {_allWords.Count(word => word.UserStatus == "Mastered")}개 · 학습 중 {_allWords.Count(word => word.UserStatus == "Learning")}개 / 서버 단어 기록 미확인"
                    : userId is null ? "로그인하면 내 학습 기록을 확인할 수 있어요." : "학습 기록을 확인하지 못했어요. 상태는 ‘미확인’으로 표시합니다.";
                ApplyFilters();
                return;
            }

            NoticeMessage = "계정이 변경되어 학습 기록을 표시하지 않았어요. 새로고침해 주세요.";
            ProgressSummary = "학습 기록 미확인";
            ApplyFilters();
        }
        catch
        {
            _allWords = [];
            Words = [];
            ResultSummary = "단어를 불러오지 못했어요";
            ProgressSummary = "학습 기록 미확인";
            EmptyTitle = "지금은 단어장을 불러올 수 없어요";
            EmptyDescription = "네트워크를 확인한 뒤 새로고침해 주세요.";
            ErrorMessage = "단어를 불러오지 못했습니다. 기존 학습 기록이 삭제된 것은 아니에요.";
        }
        finally { IsBusy = false; }
    }

    private async Task<ProgressResult> LoadProgressAsync(string? userId)
    {
        if (userId is null) return new([], false, false, false, "");
        try
        {
            var progress = await _api.GetProgressAsync(userId);
            return new(progress.GroupBy(item => item.WordId).ToDictionary(group => group.Key, group => group.First().Status), true, true, false, "");
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new([], false, false, true, "로그인 상태를 확인하지 못했어요. 다시 로그인해 주세요.");
        }
        catch
        {
            try
            {
                var local = _api.GetLocalWordProgress(userId);
                return new(local.ToDictionary(item => item.WordId, item => item.Status), false, true, false,
                    "서버 학습 기록은 확인하지 못했어요. 기본팩은 이 기기에 저장된 진도만 표시합니다.");
            }
            catch
            {
                return new([], false, false, false, "학습 기록을 읽지 못해 상태를 ‘미확인’으로 표시합니다. 기존 저장 내용은 변경하지 않았어요.");
            }
        }
    }

    private void ApplyFilters()
    {
        if (_resettingFilters) return;
        var filtered = WordLibraryQuery.Filter(_allWords, SearchText, SelectedLevel, SelectedTopic, SelectedStatus, SelectedSort);
        Words = filtered;
        ResultSummary = $"{Words.Count:N0}개 표시 / 전체 {_allWords.Count:N0}개";
        EmptyTitle = "조건에 맞는 단어가 없어요";
        EmptyDescription = "검색어를 줄이거나 필터를 초기화해 보세요. 검색은 기기 안에서 바로 적용됩니다.";
    }

    private sealed record ProgressResult(Dictionary<string, string> Statuses, bool Available, bool LocalAvailable, bool AuthenticationFailed, string Message);
}
