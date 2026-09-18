using System.Text.RegularExpressions;
using LexiFlow.Models;
using LexiFlow.Services;

namespace LexiFlow.Views;

public partial class TestWordsView : ContentPage, IQueryAttributable
{
    private static readonly Regex TokenPattern = new(@"[\p{L}']+|[^\p{L}\s]", RegexOptions.Compiled);
    private readonly ApiService _api;
    private readonly SessionService _session;
    private readonly StreakService _streak;
    private readonly LearningMetricsService _metrics;
    private readonly ArchiveService _archive;
    private readonly SentenceCatalogService _catalog;
    private readonly CourseService _course;
    private readonly Queue<SentenceExercise> _remaining = new();
    private readonly HashSet<string> _completed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Word> _serverWords = new(StringComparer.OrdinalIgnoreCase);

    private List<SentenceExercise> _lesson = [];
    private string? _selectedChoice;
    private int _firstPassCorrect;
    private int _sessionXp;
    private bool _feedbackVisible;
    private bool _isLoading;
    private bool _isGrading;
    private bool _assisted;
    private bool _completionRecorded;
    private string? _stageId;
    private SentenceExerciseKind? _practiceKind;
    private SentenceExercise? _displayedExercise;
    private string[] _tiles = [];
    private readonly List<int> _tileOrder = [];
    private readonly List<int> _selectedTiles = [];

    public TestWordsView(
        ApiService api,
        SessionService session,
        StreakService streak,
        LearningMetricsService metrics,
        ArchiveService archive,
        SentenceCatalogService catalog,
        CourseService course)
    {
        InitializeComponent();
        _api = api;
        _session = session;
        _streak = streak;
        _metrics = metrics;
        _archive = archive;
        _catalog = catalog;
        _course = course;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _stageId = query.TryGetValue("stage", out var id) && !string.IsNullOrWhiteSpace(id?.ToString()) ? id.ToString() : null;
        _practiceKind = _stageId is null && query.TryGetValue("practice", out var kind)
            && Enum.TryParse<SentenceExerciseKind>(kind?.ToString(), out var parsed)
            && Enum.IsDefined(parsed) ? parsed : null;
        _lesson.Clear();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_lesson.Count == 0 && !_isLoading)
            await StartNewLessonAsync();
    }

    private async Task StartNewLessonAsync()
    {
        if (_isLoading || _isGrading) return;
        _isLoading = true;
        loadingOverlay.IsVisible = true;
        lessonPanel.IsVisible = false;
        completionPanel.IsVisible = false;
        _serverWords.Clear();

        List<Word> words = [];
        List<WordProgress> progress = [];

        try
        {
            words = await _api.GetWordsAsync().WaitAsync(TimeSpan.FromSeconds(12));
            foreach (var word in words.Where(word => !string.IsNullOrWhiteSpace(word.English)))
                _serverWords[NormalizeWord(word.English)] = word;
        }
        catch
        {
            // The built-in sentence course remains available offline.
        }

        if (_session.IsLoggedIn)
        {
            try
            {
                progress = await _api.GetProgressAsync(_session.CurrentUserId!).WaitAsync(TimeSpan.FromSeconds(8));
            }
            catch
            {
                // Server examples can still be used without personalized progress.
            }
        }

        try
        {
            _lesson = (_stageId is null
                ? _catalog.CreateLesson(words, progress, _archive.GetAll(), 10)
                : _course.StartStage(_stageId)).ToList();
            if (_practiceKind is { } practice)
                _lesson = _lesson.Select(item => SentenceCatalogService.WithKind(item, practice)).ToList();
            var stage = _stageId is null ? null : _course.GetStages().FirstOrDefault(item => item.Id == _stageId);
            lessonTitle.Text = stage is null ? "맞춤 문장 복습" : $"STEP {stage.Number:00} · {stage.UnitTitle}";
            lessonSubtitle.Text = stage is null ? "뜻 · 빈칸 · 어순 · 작문" : $"UNIT {stage.Unit + 1} · {stage.Exercises.Count}문제";
            if (_practiceKind is not null)
            {
                lessonTitle.Text = _practiceKind == SentenceExerciseKind.ArrangeWords ? "어순 집중 연습" : "문장 쓰기 연습";
                lessonSubtitle.Text = "모르는 단어가 들어간 문장을 중심으로 10문제 연습해요.";
            }
            if (_lesson.Count == 0)
            {
                await DisplayAlertAsync("아직 잠긴 단계예요", "이전 단계를 완료한 뒤 다시 도전해 주세요.", "확인");
                await Shell.Current.GoToAsync("..");
                return;
            }
            RestartLesson();
        }
        finally
        {
            loadingOverlay.IsVisible = false;
            _isLoading = false;
        }
    }

    private void RestartLesson()
    {
        _remaining.Clear();
        foreach (var exercise in _lesson)
            _remaining.Enqueue(exercise);

        _completed.Clear();
        _seen.Clear();
        _firstPassCorrect = 0;
        _sessionXp = 0;
        _feedbackVisible = false;
        _completionRecorded = false;
        completionPanel.IsVisible = false;
        lessonPanel.IsVisible = _lesson.Count > 0;
        ShowCurrentExercise();
    }

    private void ShowCurrentExercise()
    {
        if (_remaining.Count == 0)
        {
            CompleteLesson();
            return;
        }

        var exercise = _remaining.Peek();
        _displayedExercise = exercise;
        _assisted = false;
        _selectedChoice = null;
        _feedbackVisible = false;
        wordPeekPanel.IsVisible = false;
        feedbackPanel.IsVisible = false;
        lessonActions.IsVisible = true;
        checkButton.Text = "정답 확인";
        checkButton.IsEnabled = false;
        sessionXpLabel.Text = $"+{_sessionXp} XP";
        lessonProgress.Progress = _lesson.Count == 0 ? 0 : (double)_completed.Count / _lesson.Count;
        counterLabel.Text = $"{Math.Min(_completed.Count + 1, _lesson.Count)} / {_lesson.Count}";

        var isChoice = exercise.Kind == SentenceExerciseKind.ChooseMeaning;
        var isBlank = exercise.Kind == SentenceExerciseKind.FillBlank;
        var isArrange = exercise.Kind == SentenceExerciseKind.ArrangeWords;
        var isWrite = exercise.Kind == SentenceExerciseKind.WriteSentence;
        typeLabel.Text = exercise.Kind switch
        {
            SentenceExerciseKind.ArrangeWords => "문장 순서 맞추기",
            SentenceExerciseKind.WriteSentence => "문장 전체 쓰기",
            SentenceExerciseKind.FillBlank => "빈칸 직접 쓰기",
            _ => "문장 뜻 고르기"
        };
        instructionLabel.Text = exercise.Kind switch
        {
            SentenceExerciseKind.ArrangeWords => "단어를 올바른 순서로 놓으세요",
            SentenceExerciseKind.WriteSentence => "해석에 맞는 영어 예문을 써 보세요",
            SentenceExerciseKind.FillBlank => "빈칸에 들어갈 영어 단어를 쓰세요",
            _ => "이 문장의 뜻을 고르세요"
        };
        koreanPromptBorder.IsVisible = !isChoice;
        koreanPromptLabel.Text = exercise.Korean;
        choicePanel.IsVisible = isChoice;
        writePanel.IsVisible = isBlank || isWrite;
        arrangePanel.IsVisible = isArrange;
        arrangePanel.IsEnabled = true;
        hintButton.IsVisible = isWrite || isArrange;
        hintButton.IsEnabled = true;
        hintButton.Text = "예문 힌트 보기";
        writingCue.IsVisible = isWrite;
        writingCue.Text = $"사용할 표현: {exercise.TargetWord} · {exercise.TargetMeaning}";
        sentenceTokens.IsVisible = isChoice || isBlank;
        wordHelpLabel.IsVisible = sentenceTokens.IsVisible;
        answerEntry.Text = "";
        answerEntry.Placeholder = isWrite ? "영어 문장 전체를 입력하세요" : "빠진 영어 단어를 입력하세요";
        answerEntry.IsEnabled = true;
        answerHintLabel.Text = isWrite ? "등록된 예문 기준으로 채점해요. 막히면 힌트를 보세요." : "대소문자와 문장부호는 자유롭게 써도 돼요.";
        answerHintLabel.TextColor = Color.FromArgb("#A7B2C7");

        BuildSentence(exercise, hideTarget: isBlank);
        BuildChoices(exercise);
        if (isArrange) BuildTiles(exercise);
        UpdateCheckState();
        // Do not auto-focus: on small screens the keyboard hides the question.
        _ = lessonScroll.ScrollToAsync(0, 0, false);
    }

    private void BuildSentence(SentenceExercise exercise, bool hideTarget)
    {
        sentenceTokens.Children.Clear();
        var vocabulary = exercise.Vocabulary.ToDictionary(
            item => NormalizeWord(item.Word),
            item => item,
            StringComparer.OrdinalIgnoreCase);

        foreach (Match match in TokenPattern.Matches(exercise.Sentence))
        {
            var token = match.Value;
            var normalized = NormalizeWord(token);
            if (hideTarget && normalized.Equals(NormalizeWord(exercise.TargetWord), StringComparison.OrdinalIgnoreCase))
            {
                sentenceTokens.Children.Add(new Border
                {
                    BackgroundColor = Color.FromArgb("#20345F"),
                    Stroke = Color.FromArgb("#5B8CFF"),
                    StrokeThickness = 1,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                    Padding = new Thickness(13, 3),
                    Margin = new Thickness(3, 2),
                    Content = new Label
                    {
                        Text = "______",
                        FontSize = 21,
                        TextColor = Color.FromArgb("#5B8CFF")
                    }
                });
                continue;
            }

            var hint = vocabulary.GetValueOrDefault(normalized) ?? FindServerHint(normalized);
            if (hint is not null && normalized.Length > 0)
            {
                var wordButton = new Button
                {
                    Text = token,
                    CommandParameter = hint,
                    BackgroundColor = Colors.Transparent,
                    TextColor = Color.FromArgb("#7EA5FF"),
                    FontSize = 21,
                    FontAttributes = FontAttributes.Bold,
                    HeightRequest = 38,
                    MinimumWidthRequest = 0,
                    Padding = new Thickness(3, 0),
                    Margin = new Thickness(1, 1),
                    CornerRadius = 7
                };
                wordButton.Clicked += OnSentenceWordClick;
                sentenceTokens.Children.Add(wordButton);
            }
            else
            {
                sentenceTokens.Children.Add(new Label
                {
                    Text = token,
                    FontSize = 21,
                    TextColor = Color.FromArgb("#F8FAFC"),
                    Padding = new Thickness(token.Length == 1 && !char.IsLetterOrDigit(token[0]) ? 0 : 3, 5, 0, 3),
                    Margin = new Thickness(1, 1)
                });
            }
        }
    }

    private void BuildChoices(SentenceExercise exercise)
    {
        choicePanel.Children.Clear();
        if (exercise.Kind != SentenceExerciseKind.ChooseMeaning)
            return;

        foreach (var choice in exercise.Choices.OrderBy(_ => Random.Shared.Next()))
        {
            var button = new Button
            {
                Text = choice,
                CommandParameter = choice,
                BackgroundColor = Color.FromArgb("#1C2942"),
                TextColor = Color.FromArgb("#F8FAFC"),
                BorderColor = Color.FromArgb("#2A3A58"),
                BorderWidth = 1,
                CornerRadius = 14,
                HeightRequest = -1,
                MinimumHeightRequest = 58,
                LineBreakMode = LineBreakMode.WordWrap,
                Padding = new Thickness(18, 14),
                FontSize = 16,
                HorizontalOptions = LayoutOptions.Fill
            };
            button.Clicked += OnChoiceClick;
            choicePanel.Children.Add(button);
        }
    }

    private void OnChoiceClick(object? sender, EventArgs e)
    {
        if (_feedbackVisible || sender is not Button selected || selected.CommandParameter is not string choice)
            return;

        _selectedChoice = choice;
        foreach (var child in choicePanel.Children.OfType<Button>())
        {
            var isSelected = ReferenceEquals(child, selected);
            child.BackgroundColor = Color.FromArgb(isSelected ? "#20345F" : "#1C2942");
            child.BorderColor = Color.FromArgb(isSelected ? "#5B8CFF" : "#2A3A58");
            child.BorderWidth = isSelected ? 2 : 1;
        }
        UpdateCheckState();
    }

    private void OnSentenceWordClick(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: VocabularyHint hint } || _displayedExercise is null)
            return;

        var exercise = _displayedExercise;
        var saved = _archive.Save(hint.Word, hint.Meaning, exercise.Sentence);
        peekWordLabel.Text = saved.Word;
        peekMeaningLabel.Text = saved.Meaning;
        wordPeekPanel.IsVisible = true;
    }

    private async void OnCheckClick(object? sender, EventArgs e)
    {
        if (_isGrading || _isLoading) return;
        if (_feedbackVisible)
        {
            ShowCurrentExercise();
            return;
        }

        if (_remaining.Count == 0)
            return;

        var exercise = _remaining.Peek();
        if (exercise.Kind == SentenceExerciseKind.ArrangeWords)
        {
            if (_selectedTiles.Count != _tiles.Length)
            {
                checkButton.Text = "단어를 모두 놓아 주세요";
                return;
            }
            await GradeAsync(SentenceAnswer.Matches(string.Join(" ", _selectedTiles.Select(index => _tiles[index])), exercise.Sentence));
            return;
        }
        if (exercise.Kind == SentenceExerciseKind.ChooseMeaning)
        {
            if (string.IsNullOrWhiteSpace(_selectedChoice))
            {
                checkButton.Text = "답을 하나 골라 주세요";
                return;
            }

            await GradeAsync(_selectedChoice == exercise.Korean);
            return;
        }

        var input = answerEntry.Text ?? "";
        if (string.IsNullOrWhiteSpace(input))
        {
            answerHintLabel.Text = "빈칸에 답을 입력해 주세요.";
            answerHintLabel.TextColor = Color.FromArgb("#FB7185");
            answerEntry.Focus();
            return;
        }

        var expected = exercise.Kind == SentenceExerciseKind.WriteSentence ? exercise.Sentence : exercise.TargetWord;
        await GradeAsync(SentenceAnswer.Matches(input, expected));
    }

    private async void OnDontKnowClick(object? sender, EventArgs e)
    {
        if (_feedbackVisible || _remaining.Count == 0)
            return;

        await GradeAsync(correct: false, revealed: true);
    }

    private async Task GradeAsync(bool correct, bool revealed = false)
    {
        if (_remaining.Count == 0 || _feedbackVisible || _isGrading)
            return;

        _isGrading = true;
        checkButton.IsEnabled = false;

        var exercise = _remaining.Dequeue();
        var firstEncounter = _seen.Add(exercise.Id);

        if (correct)
        {
            _completed.Add(exercise.Id);
            if (firstEncounter && !_assisted)
                _firstPassCorrect++;
        }
        else
        {
            _remaining.Enqueue(exercise);
        }

        _streak.RegisterStudyToday();
        _sessionXp += _metrics.RegisterReview(correct);
        _feedbackVisible = true;
        feedbackPanel.IsVisible = true;
        lessonActions.IsVisible = false;
        answerEntry.IsEnabled = false;
        answerEntry.Unfocus();
        arrangePanel.IsEnabled = false;
        hintButton.IsVisible = false;
        sentenceTokens.IsVisible = true;
        wordHelpLabel.IsVisible = true;
        BuildSentence(exercise, hideTarget: false);
        foreach (var button in choicePanel.Children.OfType<Button>())
            button.IsEnabled = false;

        var accent = Color.FromArgb(correct ? "#22C55E" : "#F59E0B");
        feedbackPanel.Stroke = accent;
        feedbackPanel.BackgroundColor = Color.FromArgb(correct ? "#163A2A" : "#3B2B15");
        feedbackIconBorder.BackgroundColor = accent;
        feedbackIconLabel.Text = correct ? "✓" : "!";
        feedbackTitleLabel.Text = correct ? "정답이에요!" : revealed ? "정답을 확인했어요" : "괜찮아요, 뒤에서 다시 만나요";
        feedbackBodyLabel.Text = $"{exercise.Sentence}\n{exercise.Korean}\n{exercise.TargetWord} · {exercise.TargetMeaning}";
        checkButton.Text = "계속";
        sessionXpLabel.Text = $"+{_sessionXp} XP";
        lessonProgress.Progress = _lesson.Count == 0 ? 0 : (double)_completed.Count / _lesson.Count;
        try { await SaveServerProgressAsync(exercise, correct && !_assisted); }
        finally { _isGrading = false; UpdateCheckState(); }
    }

    private async Task SaveServerProgressAsync(SentenceExercise exercise, bool correct)
    {
        if (!_session.IsLoggedIn || !_serverWords.TryGetValue(NormalizeWord(exercise.TargetWord), out var word))
            return;

        try
        {
            await _api.UpsertProgressAsync(
                _session.CurrentUserId!,
                word.Id,
                correct,
                correct ? "Mastered" : "Learning").WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Sentence lessons remain available when progress sync is offline.
        }
    }

    private void CompleteLesson()
    {
        lessonPanel.IsVisible = false;
        completionPanel.IsVisible = true;
        scoreLabel.Text = $"{_firstPassCorrect} / {_lesson.Count}";
        earnedXpLabel.Text = $"+{_sessionXp} XP";
        if (_stageId is not null && !_completionRecorded)
        {
            var stars = _course.Complete(_stageId, _firstPassCorrect, _completed.Count);
            completionStars.Text = new string('★', stars) + new string('☆', 3 - stars);
            completionTitle.Text = "단계 완료!";
            completionMessage.Text = "별점이 저장됐어요. 코스에서 다음 단계에 도전하세요.";
            nextLessonButton.Text = "코스에서 이어가기";
            _completionRecorded = true;
        }
        else if (_stageId is null)
        {
            completionStars.Text = "";
            nextLessonButton.Text = "새 문장 10개 시작";
        }
        _ = lessonScroll.ScrollToAsync(0, 0, false);
    }

    private async void OnNewLessonClick(object? sender, EventArgs e)
    {
        if (_stageId is null) await StartNewLessonAsync();
        else await Shell.Current.GoToAsync("..");
    }

    private async void OnCourseClick(object? sender, EventArgs e)
    {
        if (_isGrading || _isLoading) return;
        await Shell.Current.GoToAsync("..");
    }

    private async void OnArchiveClick(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//archive");

    private async void OnHomeClick(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//home");

    private VocabularyHint? FindServerHint(string normalized)
    {
        if (!_serverWords.TryGetValue(normalized, out var word))
            return null;

        return new VocabularyHint { Word = word.English.Trim(), Meaning = word.Meaning };
    }

    private static string NormalizeWord(string value)
        => Regex.Replace(value.Trim().ToLowerInvariant(), @"[^\p{L}']", "");

    private void OnHintClick(object? sender, EventArgs e)
    {
        if (_displayedExercise is null || _feedbackVisible || _isGrading) return;
        _assisted = true;
        sentenceTokens.IsVisible = true;
        wordHelpLabel.IsVisible = true;
        BuildSentence(_displayedExercise, hideTarget: false);
        hintButton.IsEnabled = false;
        hintButton.Text = "힌트 사용 · 별점은 첫 시도 정답 기준";
    }

    private void BuildTiles(SentenceExercise exercise)
    {
        _tiles = SentenceAnswer.Tokens(exercise.Sentence);
        _selectedTiles.Clear();
        _tileOrder.Clear();
        _tileOrder.AddRange(Enumerable.Range(0, _tiles.Length).OrderBy(_ => Random.Shared.Next()));
        if (_tiles.Length > 1 && _tileOrder.SequenceEqual(Enumerable.Range(0, _tiles.Length)))
            (_tileOrder[0], _tileOrder[1]) = (_tileOrder[1], _tileOrder[0]);
        RenderTiles();
    }

    private void RenderTiles()
    {
        selectedTokens.Children.Clear();
        availableTokens.Children.Clear();
        if (_selectedTiles.Count == 0)
            selectedTokens.Children.Add(new Label { Text = "여기에 문장을 만들어 보세요", TextColor = Color.FromArgb("#71809B"), Margin = 10 });
        foreach (var index in _selectedTiles) AddTile(selectedTokens, index, true);
        // Keep a placeholder in the word bank so other tiles never jump under the pointer.
        foreach (var index in _tileOrder) AddTile(availableTokens, index, false);
        UpdateCheckState();
    }

    private void OnAnswerTextChanged(object? sender, TextChangedEventArgs e) => UpdateCheckState();

    private void UpdateCheckState()
    {
        if (checkButton is null) return;
        var ready = _feedbackVisible || (_displayedExercise?.Kind switch
        {
            SentenceExerciseKind.ChooseMeaning => _selectedChoice is not null,
            SentenceExerciseKind.ArrangeWords => _tiles.Length > 0 && _selectedTiles.Count == _tiles.Length,
            SentenceExerciseKind.FillBlank or SentenceExerciseKind.WriteSentence => !string.IsNullOrWhiteSpace(answerEntry.Text),
            _ => false
        });
        checkButton.IsEnabled = ready && !_isGrading;
        checkButton.Opacity = checkButton.IsEnabled ? 1 : .45;
        if (!_feedbackVisible) checkButton.Text = "정답 확인";
    }

    private void AddTile(FlexLayout container, int index, bool selected)
    {
        var used = !selected && _selectedTiles.Contains(index);
        var button = new Button
        {
            Text = _tiles[index], FontSize = 16, Margin = 4, Padding = new Thickness(13, 6),
            BackgroundColor = Color.FromArgb(selected ? "#20345F" : "#1C2942"),
            TextColor = used ? Colors.Transparent : Colors.White,
            IsEnabled = !used, Opacity = used ? .25 : 1,
            CornerRadius = 12, MinimumWidthRequest = 44,
            BorderWidth = 1, BorderColor = Color.FromArgb(selected ? "#7EA5FF" : "#41516C")
        };
        button.Clicked += (_, _) =>
        {
            if (_feedbackVisible || _isGrading) return;
            if (selected) _selectedTiles.Remove(index);
            else if (!_selectedTiles.Contains(index)) _selectedTiles.Add(index);
            RenderTiles();
        };
        container.Children.Add(button);
    }

    private void OnClearTokensClick(object? sender, EventArgs e)
    {
        if (_feedbackVisible || _isGrading) return;
        _selectedTiles.Clear();
        RenderTiles();
    }
}
