using LexiFlow.Models;
using LexiFlow.Services;

namespace LexiFlow.Views;

public partial class ArchiveView : ContentPage
{
    private readonly ArchiveService _archive;
    private List<ArchivedWord> _all = [];

    public ArchiveView(ArchiveService archive)
    {
        InitializeComponent();
        _archive = archive;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Reload();
    }

    private void Reload()
    {
        _all = _archive.GetAll().ToList();
        ApplyFilter(searchEntry.Text ?? "");
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
        => ApplyFilter(e.NewTextValue ?? "");

    private void ApplyFilter(string query)
    {
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
    }

    private async void OnRemoveClick(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string word })
            return;

        var remove = await DisplayAlertAsync(
            "Archive에서 삭제",
            $"‘{word}’을 저장 목록에서 지울까요?",
            "삭제",
            "취소");

        if (!remove)
            return;

        _archive.Remove(word);
        Reload();
    }

    private async void OnPracticeClick(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//learn");
}
