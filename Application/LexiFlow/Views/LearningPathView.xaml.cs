using LexiFlow.Models;
using LexiFlow.Services;

namespace LexiFlow.Views;

public partial class LearningPathView : ContentPage
{
    private readonly ApiService _api;
    private readonly CourseService _course;
    private IReadOnlyList<CourseStage> _stages = [];
    private bool _busy;
    private bool _navigating;
    private bool _loaded;
    private int _selectedUnit = -1;
    private string? _selectedStageId;
    private bool? _wideLayout;

    public LearningPathView(ApiService api, CourseService course)
    {
        InitializeComponent();
        _api = api;
        _course = course;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_loaded) await LoadAsync();
        else { SelectNextStage(); Render(); }
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        var wide = width >= 900;
        if (_wideLayout == wide) return;
        _wideLayout = wide;
        courseLayout.ColumnDefinitions.Clear();
        courseLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        if (wide) courseLayout.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(340)));
        Grid.SetRow(pathCard, wide ? 0 : 1);
        Grid.SetRow(detailColumn, 0);
        Grid.SetColumn(detailColumn, wide ? 1 : 0);
    }

    private void SelectNextStage()
    {
        var next = _stages.FirstOrDefault(stage => _course.Stars(stage.Id) == 0) ?? _stages.LastOrDefault();
        _selectedUnit = next?.Unit ?? 0;
        _selectedStageId = next?.Id;
    }

    private async Task LoadAsync()
    {
        if (_busy) return;
        _busy = true;
        loading.IsVisible = loading.IsRunning = true;
        retryButton.IsVisible = false;
        try
        {
            var words = await _api.GetWordsAsync().WaitAsync(TimeSpan.FromSeconds(12));
            _stages = _course.GetStages(words);
            statusLabel.IsVisible = false;
        }
        catch
        {
            _stages = _course.GetStages();
            statusLabel.Text = "연결이 원활하지 않아 저장된 코스로 이어갑니다.";
            statusLabel.IsVisible = true;
            retryButton.IsVisible = true;
        }
        finally
        {
            _busy = false;
            _loaded = true;
            loading.IsVisible = loading.IsRunning = false;
            SelectNextStage();
            Render();
        }
    }

    private void Render()
    {
        stageList.Children.Clear();
        unitTabs.Children.Clear();
        var done = _stages.Count(stage => _course.Stars(stage.Id) > 0);
        progressLabel.Text = done == _stages.Count && done > 0 ? "모든 단계를 완주했어요. 별 3개에 다시 도전해 보세요!" : $"{done} / {_stages.Count} 단계 완료 · 짧게 배우고, 오래 기억해요";
        starsLabel.Text = $"★ {_stages.Sum(stage => _course.Stars(stage.Id))}";
        courseProgress.Progress = _stages.Count == 0 ? 0 : (double)done / _stages.Count;
        foreach (var unit in _stages.GroupBy(stage => stage.Unit))
        {
            var index = unit.Key;
            var complete = unit.All(stage => _course.Stars(stage.Id) > 0);
            var tab = new Button
            {
                Text = $"UNIT {index + 1}" + (complete ? "  ✓" : ""),
                HeightRequest = 44, Padding = new Thickness(18, 0), FontSize = 13,
                BackgroundColor = Color.FromArgb(index == _selectedUnit ? "#5B8CFF" : "#151F35"),
                TextColor = Color.FromArgb(index == _selectedUnit ? "#FFFFFF" : "#A7B2C7"),
                BorderWidth = 1, BorderColor = Color.FromArgb(index == _selectedUnit ? "#7EA5FF" : "#2A3A58")
            };
            SemanticProperties.SetDescription(tab, $"유닛 {index + 1}, {unit.First().UnitTitle}" + (index == _selectedUnit ? ", 선택됨" : ""));
            tab.Clicked += (_, _) =>
            {
                _selectedUnit = index;
                _selectedStageId = unit.FirstOrDefault(stage => _course.Stars(stage.Id) == 0)?.Id ?? unit.First().Id;
                Render();
            };
            unitTabs.Children.Add(tab);
        }
        var visibleStages = _stages.Where(stage => stage.Unit == _selectedUnit).ToList();
        var first = visibleStages.FirstOrDefault();
        unitEyebrow.Text = $"UNIT {_selectedUnit + 1:00}";
        unitTitle.Text = first?.UnitTitle ?? "아직 준비된 코스가 없어요";
        unitTip.Text = first?.Tip;
        unitProgressLabel.Text = $"{visibleStages.Count(stage => _course.Stars(stage.Id) > 0)} / {visibleStages.Count}";
        foreach (var stage in visibleStages)
        {
            var unlocked = _course.IsUnlocked(stage, _stages);
            var stars = _course.Stars(stage.Id);
            var selected = stage.Id == _selectedStageId;
            var row = new Grid { ColumnDefinitions = [new ColumnDefinition(new GridLength(64)), new ColumnDefinition(GridLength.Star)], ColumnSpacing = 18, Padding = new Thickness(0, 8) };
            var node = new Button
            {
                Text = stars > 0 ? "✓" : stage.Number.ToString("00"), FontSize = 21,
                HeightRequest = 64, WidthRequest = 64, CornerRadius = 32, Padding = 0,
                BackgroundColor = Color.FromArgb(stars > 0 ? "#1B714A" : unlocked ? "#5B8CFF" : "#202C40"),
                TextColor = Color.FromArgb(unlocked ? "#FFFFFF" : "#8998B2"),
                BorderWidth = selected ? 3 : 1, BorderColor = Color.FromArgb(selected ? "#B0CCFF" : "#354560")
            };
            SemanticProperties.SetDescription(node, $"{stage.Number}단계, {(stars > 0 ? "완료" : unlocked ? "학습 가능" : "잠김")}, 내용 보기");
            node.Clicked += async (_, _) =>
            {
                _selectedStageId = stage.Id;
                Render();
                if (_wideLayout == false)
                    await courseScroll.ScrollToAsync(detailColumn, ScrollToPosition.Start, true);
            };
            var info = new VerticalStackLayout { Spacing = 5, VerticalOptions = LayoutOptions.Center };
            info.Children.Add(new Label { Text = $"STEP {stage.Number:00}" + (selected ? "  · 선택됨" : ""), FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(unlocked ? "#F8FAFC" : "#A7B2C7") });
            info.Children.Add(new Label { Text = stars > 0 ? new string('★', stars) + new string('☆', 3 - stars) : unlocked ? "지금 도전할 차례" : "이전 단계 완료 후 열려요", FontSize = 13, TextColor = Color.FromArgb(stars > 0 ? "#FBBF24" : unlocked ? "#93B7FF" : "#8998B2") });
            row.Add(node);
            row.Add(info, 1);
            stageList.Children.Add(row);
            if (stage != visibleStages.Last())
                stageList.Children.Add(new BoxView { WidthRequest = 3, HeightRequest = 24, HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(30, 0, 0, 0), Color = Color.FromArgb(stars > 0 ? "#22C55E" : "#354560") });
        }
        RenderSelection();
    }

    private void RenderSelection()
    {
        var stage = _stages.FirstOrDefault(item => item.Id == _selectedStageId);
        wordChips.Children.Clear();
        continueButton.IsEnabled = stage is not null && _course.IsUnlocked(stage, _stages);
        if (stage is null) return;
        var stars = _course.Stars(stage.Id);
        var unlocked = _course.IsUnlocked(stage, _stages);
        selectedEyebrow.Text = stars > 0 ? "ANOTHER TRY, MORE CONFIDENCE" : unlocked ? "YOUR NEXT STEP" : "COMING UP NEXT";
        selectedTitle.Text = $"STEP {stage.Number:00} · " + (stars > 0 ? "다시 연습" : unlocked ? "시작하기" : "미리 보기");
        selectedDescription.Text = stage.Unit switch
        {
            0 => "짧은 문장을 읽고 뜻과 어순에 익숙해져요.",
            1 => "단어를 연결하고, 빈칸을 채우며 표현을 익혀요.",
            2 => "생각한 문장을 영어로 직접 써 보세요.",
            _ => "조금 더 긴 문장으로 배운 표현을 활용해요."
        };
        foreach (var word in stage.Exercises.Select(item => item.TargetWord).Distinct().Take(4))
            wordChips.Children.Add(new Border
            {
                Padding = new Thickness(10, 6), Margin = new Thickness(0, 0, 6, 6),
                BackgroundColor = Color.FromArgb("#243D61"), StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                Content = new Label { Text = word, FontSize = 13, TextColor = Color.FromArgb("#D8E7FF") }
            });
        selectedMeta.Text = $"{stage.Exercises.Count}문제 · " + (stage.Unit == 0 ? "뜻 · 어순 · 빈칸" : "뜻 · 어순 · 빈칸 · 작문");
        continueButton.Text = !unlocked ? "아직 잠긴 단계예요" : stars > 0 ? "다시 연습하기  →" : "학습 시작  →";
        continueButton.Opacity = unlocked ? 1 : .5;
        selectionHint.Text = !unlocked ? $"STEP {stage.Number - 1:00}을 완료하면 열려요." : stars > 0 ? "다시 풀어도 가장 높은 별점은 유지돼요." : "틀려도 괜찮아요. 다시 풀며 완성하면 돼요.";
    }

    private async Task OpenAsync(string? stageId, SentenceExerciseKind? practiceKind = null)
    {
        if (_navigating || _busy) return;
        _navigating = true;
        try
        {
            await Shell.Current.GoToAsync(nameof(TestWordsView), new Dictionary<string, object> { ["stage"] = stageId ?? "", ["practice"] = practiceKind?.ToString() ?? "" });
        }
        finally { _navigating = false; }
    }

    private async void OnContinueClick(object? sender, EventArgs e)
    {
        var stage = _stages.FirstOrDefault(item => item.Id == _selectedStageId);
        if (stage is not null && _course.IsUnlocked(stage, _stages)) await OpenAsync(stage.Id);
    }
    private async void OnReviewClick(object? sender, EventArgs e) => await OpenAsync(null);
    private async void OnArrangePracticeClick(object? sender, EventArgs e) => await OpenAsync(null, SentenceExerciseKind.ArrangeWords);
    private async void OnWritingPracticeClick(object? sender, EventArgs e) => await OpenAsync(null, SentenceExerciseKind.WriteSentence);
    private async void OnRetryClick(object? sender, EventArgs e) { _loaded = false; await LoadAsync(); }
}
