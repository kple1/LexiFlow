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
    private string? _loadedAccount;
    private int _selectedUnit = -1;
    private string? _selectedStageId;
    private bool? _wideLayout;
    private int[] _unitIds = [];
    private bool _updatingUnitPicker;
    private Button? _selectedUnitTab;
    private int _unitRenderVersion;

    public LearningPathView(ApiService api, CourseService course)
    {
        InitializeComponent();
        _api = api;
        _course = course;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loadedAccount != _course.AccountKey)
        {
            _loaded = false;
            _stages = [];
            ResetUnitNavigation();
        }
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
        var account = _course.AccountKey;
        loading.IsVisible = loading.IsRunning = true;
        retryButton.IsVisible = false;
        try
        {
            var words = await _api.GetWordsAsync();
            if (account != _course.AccountKey) { _stages = []; return; }
            _stages = _course.GetStages(words);
            statusLabel.Text = _api.LastWordLoadUsedFallback ? "오프라인 학습팩 사용 중 · 다시 불러오면 서버 단어를 확인합니다." : $"기본 학습팩 포함 {words.Count:N0}개 단어";
            statusLabel.IsVisible = true;
            retryButton.IsVisible = _api.LastWordLoadUsedFallback;
        }
        catch
        {
            try
            {
                _stages = _course.GetStages();
                statusLabel.Text = "연결할 수 없어 저장된 코스를 표시합니다.";
                statusLabel.IsVisible = true;
                retryButton.IsVisible = true;
            }
            catch { ShowStorageError(); }
        }
        finally
        {
            _busy = false;
            _loaded = account == _course.AccountKey;
            _loadedAccount = account;
            loading.IsVisible = loading.IsRunning = false;
            try { SelectNextStage(); Render(); }
            catch { ShowStorageError(); }
        }
    }

    private void Render()
    {
        try { RenderCore(); }
        catch { ShowStorageError(); }
    }

    private void ShowStorageError()
    {
        _stages = [];
        _loaded = false;
        ResetUnitNavigation();
        unitCountLabel.Text = "유닛 확인 불가";
        progressLabel.Text = "학습 기록 확인 불가";
        starsLabel.Text = "—";
        statusLabel.Text = "코스 기록을 읽거나 저장할 수 없습니다. 기존 기록은 유지됩니다. 저장 공간을 확인한 뒤 다시 시도하세요.";
        statusLabel.IsVisible = retryButton.IsVisible = true;
    }

    private void ResetUnitNavigation()
    {
        stageList.Children.Clear();
        unitTabs.Children.Clear();
        _unitIds = [];
        _selectedUnit = -1;
        _selectedStageId = null;
        _selectedUnitTab = null;
        _unitRenderVersion++;
        _updatingUnitPicker = true;
        try { unitPicker.ItemsSource = null; unitPicker.SelectedIndex = -1; }
        finally { _updatingUnitPicker = false; }
        unitPicker.IsEnabled = previousUnitButton.IsEnabled = nextUnitButton.IsEnabled = false;
        unitCountLabel.Text = "유닛 불러오는 중";
        continueButton.IsEnabled = false;
        RenderSelection();
    }

    private void RenderCore()
    {
        stageList.Children.Clear();
        unitTabs.Children.Clear();
        _selectedUnitTab = null;
        var renderVersion = ++_unitRenderVersion;
        var units = _stages.GroupBy(stage => stage.Unit).OrderBy(unit => unit.Key).ToArray();
        _unitIds = units.Select(unit => unit.Key).ToArray();
        if (_unitIds.Length > 0 && !_unitIds.Contains(_selectedUnit))
        {
            _selectedUnit = _unitIds[0];
            _selectedStageId = units[0].First().Id;
        }
        var selectedUnitIndex = Array.IndexOf(_unitIds, _selectedUnit);
        unitCountLabel.Text = _unitIds.Length == 0 ? "유닛 없음"
            : $"전체 {_unitIds.Length}개 유닛 · 마지막 유닛 {_unitIds[^1] + 1}";
        _updatingUnitPicker = true;
        try
        {
            // Assign selection after ItemsSource: native Picker initialization may reset it.
            unitPicker.ItemsSource = units.Select(unit => $"유닛 {unit.Key + 1} · {unit.First().UnitTitle}").ToArray();
            unitPicker.SelectedIndex = selectedUnitIndex;
        }
        finally { _updatingUnitPicker = false; }
        unitPicker.IsEnabled = _unitIds.Length > 0;
        previousUnitButton.IsEnabled = selectedUnitIndex > 0;
        nextUnitButton.IsEnabled = selectedUnitIndex >= 0 && selectedUnitIndex < _unitIds.Length - 1;
        var done = _stages.Count(stage => _course.Stars(stage.Id) > 0);
        progressLabel.Text = $"{done} / {_stages.Count} 단계 완료";
        starsLabel.Text = $"★ {_stages.Sum(stage => _course.Stars(stage.Id))}";
        courseProgress.Progress = _stages.Count == 0 ? 0 : (double)done / _stages.Count;
        foreach (var unit in units)
        {
            var index = unit.Key;
            var complete = unit.All(stage => _course.Stars(stage.Id) > 0);
            var tab = new Button
            {
                Text = $"유닛 {index + 1}" + (complete ? "  ✓" : ""),
                HeightRequest = DeviceInfo.Idiom == DeviceIdiom.Phone ? 44 : 36,
                Padding = new Thickness(12, 0), FontSize = 13, CornerRadius = 6,
                BackgroundColor = ThemeColors.Get(index == _selectedUnit ? "PrimarySoft" : "Surface"),
                TextColor = ThemeColors.Get(index == _selectedUnit ? "Primary" : "TextSecondary"),
                BorderWidth = 1, BorderColor = ThemeColors.Get(index == _selectedUnit ? "Primary" : "Stroke")
            };
            SemanticProperties.SetDescription(tab, $"유닛 {index + 1}, {unit.First().UnitTitle}" + (index == _selectedUnit ? ", 선택됨" : ""));
            tab.Clicked += (_, _) => SelectUnit(index);
            unitTabs.Children.Add(tab);
            if (index == _selectedUnit)
            {
                _selectedUnitTab = tab;
                tab.SizeChanged += OnUnitStripSizeChanged;
            }
        }
        var visibleStages = _stages.Where(stage => stage.Unit == _selectedUnit).ToList();
        var first = visibleStages.FirstOrDefault();
        unitEyebrow.Text = $"유닛 {_selectedUnit + 1}";
        unitTitle.Text = first?.UnitTitle ?? "코스 없음";
        unitTip.Text = first?.Tip;
        unitProgressLabel.Text = $"{visibleStages.Count(stage => _course.Stars(stage.Id) > 0)} / {visibleStages.Count}";
        foreach (var stage in visibleStages)
        {
            var unlocked = _course.IsUnlocked(stage, _stages);
            var stars = _course.Stars(stage.Id);
            var selected = stage.Id == _selectedStageId;
            var row = new Grid
            {
                ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
                ColumnSpacing = 10, Padding = new Thickness(8)
            };
            var node = new Button
            {
                Text = stars > 0 ? "✓" : stage.Number.ToString("00"), FontSize = 14,
                HeightRequest = DeviceInfo.Idiom == DeviceIdiom.Phone ? 44 : 36,
                WidthRequest = DeviceInfo.Idiom == DeviceIdiom.Phone ? 44 : 36,
                CornerRadius = 6, Padding = 0,
                BackgroundColor = ThemeColors.Get(stars > 0 ? "SuccessSoft" : "SurfaceElevated"),
                TextColor = ThemeColors.Get(stars > 0 ? "Success" : unlocked ? "TextPrimary" : "TextSecondary"),
                BorderWidth = 0
            };
            SemanticProperties.SetDescription(node, $"{stage.Number}단계, {(stars > 0 ? "완료" : unlocked ? "학습 가능" : "잠김")}, 내용 보기");
            async Task SelectStageAsync()
            {
                _selectedStageId = stage.Id;
                Render();
                if (_wideLayout == false)
                    await courseScroll.ScrollToAsync(detailColumn, ScrollToPosition.Start, true);
            }
            node.Clicked += async (_, _) => await SelectStageAsync();
            var info = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
            info.Children.Add(new Label { Text = $"{stage.Number}단계" + (selected ? " · 선택됨" : ""), FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = ThemeColors.Get(unlocked ? "TextPrimary" : "TextSecondary") });
            info.Children.Add(new Label { Text = stars > 0 ? "완료" : unlocked ? "학습 가능" : "이전 단계 완료 필요", FontSize = 12, TextColor = ThemeColors.Get(stars > 0 ? "Success" : "TextMuted") });
            row.Add(node);
            row.Add(info, 1);
            row.Add(new Label
            {
                Text = stars > 0 ? new string('★', stars) + new string('☆', 3 - stars) : "",
                FontSize = 12, TextColor = ThemeColors.Get("Warning"), VerticalOptions = LayoutOptions.Center
            }, 2);
            var stageRow = new Border
            {
                BackgroundColor = ThemeColors.Get(selected ? "PrimarySoft" : "Surface"),
                Stroke = ThemeColors.Get(selected ? "Primary" : "Stroke"), StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
                Content = row
            };
            var selectTap = new TapGestureRecognizer();
            selectTap.Tapped += async (_, _) => await SelectStageAsync();
            // Keep the preview tap separate from the numbered button's Clicked event.
            info.GestureRecognizers.Add(selectTap);
            stageList.Children.Add(stageRow);
        }
        RenderSelection();
        Dispatcher.Dispatch(async () =>
        {
            if (renderVersion == _unitRenderVersion) await RevealSelectedUnitAsync();
        });
    }

    private void SelectUnit(int unitId)
    {
        if (_busy || !_loaded || _loadedAccount != _course.AccountKey) return;
        var stages = _stages.Where(stage => stage.Unit == unitId).ToArray();
        if (stages.Length == 0 || unitId == _selectedUnit) return;
        _selectedUnit = unitId;
        _selectedStageId = stages.FirstOrDefault(stage => _course.Stars(stage.Id) == 0)?.Id ?? stages[0].Id;
        // Browsing a locked unit only previews it; the existing stage lock stays in force.
        Render();
    }

    private void OnUnitPickerChanged(object? sender, EventArgs e)
    {
        if (_updatingUnitPicker) return;
        var index = unitPicker.SelectedIndex;
        if (index >= 0 && index < _unitIds.Length) SelectUnit(_unitIds[index]);
    }

    private void MoveUnit(int offset)
    {
        var currentIndex = Array.IndexOf(_unitIds, _selectedUnit);
        if (currentIndex < 0) return;
        var index = currentIndex + offset;
        if (index >= 0 && index < _unitIds.Length) SelectUnit(_unitIds[index]);
    }

    private void OnPreviousUnitClick(object? sender, EventArgs e) => MoveUnit(-1);
    private void OnNextUnitClick(object? sender, EventArgs e) => MoveUnit(1);

    private async void OnUnitStripSizeChanged(object? sender, EventArgs e)
        => await RevealSelectedUnitAsync();

    private async Task RevealSelectedUnitAsync()
    {
        var tab = _selectedUnitTab;
        if (!_loaded || _loadedAccount != _course.AccountKey || tab is null
            || tab.Parent != unitTabs || tab.Width <= 0 || unitTabsScroll.Width <= 0) return;
        await unitTabsScroll.ScrollToAsync(tab, ScrollToPosition.MakeVisible, animated: false);
    }

    private void RenderSelection()
    {
        var stage = _stages.FirstOrDefault(item => item.Id == _selectedStageId);
        wordChips.Children.Clear();
        continueButton.IsEnabled = stage is not null && _course.IsUnlocked(stage, _stages);
        if (stage is null)
        {
            selectedEyebrow.Text = "선택한 단계";
            selectedTitle.Text = "코스 없음";
            selectedDescription.Text = selectedMeta.Text = selectionHint.Text = "";
            return;
        }
        var stars = _course.Stars(stage.Id);
        var unlocked = _course.IsUnlocked(stage, _stages);
        selectedEyebrow.Text = stars > 0 ? "완료한 단계" : unlocked ? "선택한 단계" : "잠긴 단계";
        selectedTitle.Text = $"{stage.Number}단계";
        selectedDescription.Text = stage.Unit switch
        {
            0 => "짧은 문장의 뜻과 어순을 연습합니다.",
            1 => "어순과 빈칸 문제로 표현을 익힙니다.",
            2 => "영어 문장을 직접 작성합니다.",
            _ => "다양한 문장으로 표현을 연습합니다."
        };
        foreach (var word in stage.Exercises.Select(item => item.TargetWord).Distinct().Take(4))
            wordChips.Children.Add(new Border
            {
                Padding = new Thickness(8, 4), Margin = new Thickness(0, 0, 6, 4),
                BackgroundColor = ThemeColors.Get("SurfaceElevated"), StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
                Content = new Label { Text = word, FontSize = 12, TextColor = ThemeColors.Get("TextSecondary") }
            });
        selectedMeta.Text = $"{stage.Exercises.Count}문제 · " + (stage.Unit == 0 ? "뜻 · 어순 · 빈칸" : "뜻 · 어순 · 빈칸 · 작문");
        continueButton.Text = !unlocked ? "이전 단계 완료 필요" : stars > 0 ? "다시 풀기" : "학습 시작";
        continueButton.Opacity = unlocked ? 1 : .5;
        selectionHint.Text = !unlocked ? $"{stage.Number - 1}단계를 완료하면 시작할 수 있습니다." : stars > 0 ? "가장 높은 별점을 유지합니다." : "오답은 이번 학습에서 다시 출제됩니다.";
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
    private async void OnMistakePracticeClick(object? sender, EventArgs e)
    {
        if (_navigating || _busy) return;
        _navigating = true;
        try { await Shell.Current.GoToAsync(nameof(TestWordsView), new Dictionary<string, object> { ["review"] = "mistakes" }); }
        finally { _navigating = false; }
    }
    private async void OnArrangePracticeClick(object? sender, EventArgs e) => await OpenAsync(null, SentenceExerciseKind.ArrangeWords);
    private async void OnWritingPracticeClick(object? sender, EventArgs e) => await OpenAsync(null, SentenceExerciseKind.WriteSentence);
    private async void OnRetryClick(object? sender, EventArgs e) { _loaded = false; await LoadAsync(); }
}
