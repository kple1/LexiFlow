using LexiFlow.Models;
using LexiFlow.Services;

namespace LexiFlow.Views;

public partial class ArchiveView : ContentPage
{
    private readonly ArchiveService _archive;
    private readonly SessionService _session;
    private List<ArchivedWord> _all = [];
    private bool _loadFailed = true;
    private bool _removing;

    public ArchiveView(ArchiveService archive, SessionService session)
    {
        InitializeComponent();
        _archive = archive;
        _session = session;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _session.StateChanged -= OnSessionChanged;
        _session.StateChanged += OnSessionChanged;
        Reload();
    }

    protected override void OnDisappearing()
    {
        _session.StateChanged -= OnSessionChanged;
        base.OnDisappearing();
    }

    private void OnSessionChanged(object? sender, EventArgs e) => MainThread.BeginInvokeOnMainThread(Reload);
    private void OnReloadClick(object? sender, EventArgs e) => Reload();

    private void Reload()
    {
        // Discard the previous screen's items before reading a potentially different account.
        _all = [];
        archiveList.ItemsSource = null;
        try
        {
            _all = _archive.GetAll().ToList();
            _loadFailed = false;
            storageErrorBorder.IsVisible = false;
            archiveList.IsVisible = true;
            searchEntry.IsEnabled = true;
            ApplyFilter(searchEntry.Text ?? "");
        }
        catch
        {
            ShowStorageError("이 기기의 Archive를 읽지 못했어요. 저장 내용을 초기화하거나 자동 복구하지 않았습니다. 다시 불러와 확인해 주세요.");
        }
    }

    private void ShowStorageError(string message)
    {
        _loadFailed = true;
        _all = [];
        archiveList.ItemsSource = null;
        archiveList.IsVisible = false;
        searchEntry.IsEnabled = false;
        summaryLabel.Text = "저장 내용 확인 필요";
        storageErrorLabel.Text = message;
        storageErrorBorder.IsVisible = true;
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
        => ApplyFilter(e.NewTextValue ?? "");

    private void ApplyFilter(string query)
    {
        if (_loadFailed) return;
        var normalized = query.Trim();
        var filtered = normalized.Length == 0
            ? _all
            : _all.Where(item =>
                    item.Word.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                    item.Meaning.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                .ToList();

        archiveList.ItemsSource = filtered;
        summaryLabel.Text = normalized.Length == 0
            ? $"저장된 단어 {_all.Count}개"
            : $"검색 결과 {filtered.Count}개";
        emptyTitleLabel.Text = _all.Count == 0 ? "아직 저장한 단어가 없어요" : "검색한 단어가 없어요";
        emptyDescriptionLabel.Text = _all.Count == 0
            ? "문장 학습에서 단어를 누르거나 단어 상세에서 Archive에 저장해 보세요."
            : "다른 검색어를 입력하거나 검색어를 지워 전체 목록을 확인하세요.";
    }

    private async void OnRemoveClick(object? sender, EventArgs e)
    {
        if (_loadFailed || _removing || sender is not Button { CommandParameter: string word })
            return;

        _removing = true;
        var account = _session.StorageId;
        var token = _session.AccessToken;
        try
        {
            var remove = await DisplayAlertAsync("Archive에서 삭제", $"‘{word}’을 저장 목록에서 지울까요?", "삭제", "취소");
            if (!remove) return;
            if (account != _session.StorageId || token != _session.AccessToken)
            {
                Reload();
                await DisplayAlertAsync("계정이 변경되었어요", "다른 계정의 단어를 삭제하지 않도록 요청을 취소했습니다.", "확인");
                return;
            }
            _archive.Remove(word);
            Reload();
        }
        catch
        {
            ShowStorageError("삭제 완료 여부를 확인하지 못했어요. 다시 불러와 저장 목록을 확인해 주세요. 다른 항목은 자동으로 지우지 않습니다.");
        }
        finally { _removing = false; }
    }

    private async void OnPracticeClick(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//learn");
}
