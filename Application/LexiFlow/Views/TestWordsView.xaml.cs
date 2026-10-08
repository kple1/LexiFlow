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
    private readonly ChatGptConnectionService _chatGpt;
    private readonly ChatGptWritingService _writing;
    private readonly Queue<SentenceExercise> _remaining = new();
    private readonly HashSet<string> _completed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _mistakes = new(StringComparer.OrdinalIgnoreCase);
    private readonly SentenceReviewService _review;
    private bool _mistakesOnly;
    private string? _lessonAccount;
    private readonly Dictionary<string, Word> _serverWords = new(StringComparer.OrdinalIgnoreCase);

    private List<SentenceExercise> _lesson = [];
    private string? _selectedChoice;
    private int _firstPassCorrect;
    private int _sessionXp;
    private bool _feedbackVisible;
    private bool _isLoading;
    private bool _isGrading;
    private bool _isEvaluating;
    private bool _viewActive;
    private CancellationTokenSource? _writingCancellation;
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
        CourseService course,
        ChatGptConnectionService chatGpt,
        ChatGptWritingService writing)
    {
        InitializeComponent();
        _api = api;
        _session = session;
        _streak = streak;
        _metrics = metrics;
        _archive = archive;
        _catalog = catalog;
        _course = course;
        _chatGpt = chatGpt;
        _writing = writing;
        _review = new SentenceReviewService(session);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _stageId = query.TryGetValue("stage", out var id) && !string.IsNullOrWhiteSpace(id?.ToString()) ? id.ToString() : null;
        _mistakesOnly = query.TryGetValue("review", out var mode) && mode?.ToString() == "mistakes";
        _practiceKind = _stageId is null && query.TryGetValue("practice", out var kind)
            && Enum.TryParse<SentenceExerciseKind>(kind?.ToString(), out var parsed)
            && Enum.IsDefined(parsed) ? parsed : null;
        _lesson.Clear();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _viewActive = true;
        try { await _chatGpt.RestoreAsync(); }
        catch { /* The disconnected state remains visible; never fall back to exact-match grading. */ }
        if (_lessonAccount != _session.StorageId) _lesson.Clear();
        if (_lesson.Count == 0 && !_isLoading)
            await StartNewLessonAsync();
        else if (_displayedExercise?.Kind == SentenceExerciseKind.WriteSentence)
            UpdateWritingConnectionState();
    }

    protected override void OnDisappearing()
    {
        _viewActive = false;
        _writingCancellation?.Cancel();
        base.OnDisappearing();
    }

    private async Task StartNewLessonAsync()
    {
        if (_isLoading || _isGrading || _isEvaluating) return;
        _isLoading = true;
        loadingOverlay.IsVisible = true;
        lessonPanel.IsVisible = false;
        completionPanel.IsVisible = false;
        feedbackPanel.IsVisible = false;
        _serverWords.Clear();
        _lessonAccount = _session.StorageId;
        syncStatusLabel.IsVisible = false;

        List<Word> words = [];
        List<WordProgress> progress = [];

        try
        {
            words = await _api.GetWordsAsync();
            foreach (var word in words.Where(word => !string.IsNullOrWhiteSpace(word.English)))
                _serverWords[NormalizeWord(word.English)] = word;
        }
        catch
        {
            words = BuiltInVocabulary.GetWords();
            foreach (var word in words) _serverWords[NormalizeWord(word.English)] = word;
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
            if (_lessonAccount != _session.StorageId) { await Shell.Current.GoToAsync("//home"); return; }
            _lesson = (_stageId is null
                ? _mistakesOnly ? _catalog.CreateMistakeLesson(words) : _catalog.CreateLesson(words, progress, _archive.GetAll(), 10)
                : _course.StartStage(_stageId)).ToList();
            if (_practiceKind is { } practice)
                _lesson = _lesson.Select(item => SentenceCatalogService.WithKind(item, practice)).ToList();
            var stage = _stageId is null ? null : _course.GetStages().FirstOrDefault(item => item.Id == _stageId);
            lessonTitle.Text = stage is null ? "맞춤 문장 복습" : $"{stage.Number}단계 · {stage.UnitTitle}";
            lessonSubtitle.Text = stage is null ? "뜻 · 빈칸 · 어순 · 작문" : $"유닛 {stage.Unit + 1} · {stage.Exercises.Count}문제";
            if (_mistakesOnly) { lessonTitle.Text = "오답 복습"; lessonSubtitle.Text = "이 기기에 저장된 오답 기준"; }
            if (_practiceKind is not null)
            {
                lessonTitle.Text = _practiceKind == SentenceExerciseKind.ArrangeWords ? "어순 연습" : "문장 쓰기";
                lessonSubtitle.Text = "미학습 단어 중심 · 10문제";
            }
            if (_lesson.Count == 0)
            {
                await DisplayAlertAsync(_mistakesOnly ? "오답 없음" : "잠긴 단계",
                    _mistakesOnly ? "복습할 오답이 없습니다. 새 학습을 시작하세요." : "이전 단계를 완료한 뒤 다시 시작하세요.", "확인");
                await Shell.Current.GoToAsync("..");
                return;
            }
            RestartLesson();
        }
        catch
        {
            await DisplayAlertAsync("학습을 열 수 없음", "학습 기록을 읽지 못했습니다. 기존 기록은 유지됩니다. 저장 공간을 확인한 뒤 다시 시도하세요.", "확인");
            await Shell.Current.GoToAsync("//learn");
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
        _mistakes.Clear();
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
        writingFeedbackDetails.IsVisible = false;
        lessonActions.IsVisible = true;
        lessonActions.IsEnabled = true;
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
            SentenceExerciseKind.ArrangeWords => "어순 맞추기",
            SentenceExerciseKind.WriteSentence => "문장 쓰기",
            SentenceExerciseKind.FillBlank => "빈칸 채우기",
            _ => "뜻 고르기"
        };
        if (!string.IsNullOrEmpty(exercise.Topic)) typeLabel.Text += $" · {exercise.Level} · {exercise.Topic}";
        instructionLabel.Text = exercise.Kind switch
        {
            SentenceExerciseKind.ArrangeWords => "단어를 올바른 순서로 놓으세요",
            SentenceExerciseKind.WriteSentence => "같은 뜻이 되도록 영어로 표현하세요",
            SentenceExerciseKind.FillBlank => "빈칸에 들어갈 단어를 쓰세요",
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
        hintButton.Text = "예문 힌트";
        writingCue.IsVisible = isWrite;
        writingCue.Text = $"참고 단어: {exercise.TargetWord} · {exercise.TargetMeaning} (다른 표현도 가능)";
        sentenceTokens.IsVisible = isChoice || isBlank;
        wordHelpLabel.IsVisible = sentenceTokens.IsVisible;
        answerEntry.Text = "";
        answerEntry.Placeholder = isWrite ? "영어 문장 입력" : "영어 단어 입력";
        answerEntry.IsEnabled = true;
        answerHintLabel.Text = isWrite
            ? "예문과 달라도 뜻이 같으면 인정합니다. 작은 문법·표현 차이는 개선 제안으로 안내합니다."
            : "대소문자와 문장부호는 채점에 영향을 주지 않습니다.";
        answerHintLabel.TextColor = ThemeColors.Get("TextSecondary");
        writingAiPanel.IsVisible = isWrite;
        writingActivity.IsRunning = writingActivity.IsVisible = false;

        BuildSentence(exercise, hideTarget: isBlank);
        BuildChoices(exercise);
        if (isArrange) BuildTiles(exercise);
        UpdateCheckState();
        if (isWrite) UpdateWritingConnectionState();
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
                    BackgroundColor = ThemeColors.Get("PrimarySoft"),
                    Stroke = ThemeColors.Get("Primary"),
                    StrokeThickness = 1,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
                    Padding = new Thickness(10, 4),
                    Margin = new Thickness(2, 2),
                    Content = new Label
                    {
                        Text = "______",
                        FontSize = 18,
                        TextColor = ThemeColors.Get("Primary")
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
                    TextColor = ThemeColors.Get("Primary"),
                    FontSize = 18,
                    FontAttributes = FontAttributes.Bold,
                    HeightRequest = DeviceInfo.Idiom == DeviceIdiom.Phone ? 44 : 36,
                    MinimumWidthRequest = 0,
                    Padding = new Thickness(3, 0),
                    Margin = new Thickness(1, 1),
                    CornerRadius = 6
                };
                wordButton.Clicked += OnSentenceWordClick;
                sentenceTokens.Children.Add(wordButton);
            }
            else
            {
                sentenceTokens.Children.Add(new Label
                {
                    Text = token,
                    FontSize = 18,
                    TextColor = ThemeColors.Get("TextPrimary"),
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
                BackgroundColor = ThemeColors.Get("Surface"),
                TextColor = ThemeColors.Get("TextPrimary"),
                BorderColor = ThemeColors.Get("Stroke"),
                BorderWidth = 1,
                CornerRadius = 6,
                HeightRequest = -1,
                MinimumHeightRequest = DeviceInfo.Idiom == DeviceIdiom.Phone ? 44 : 36,
                LineBreakMode = LineBreakMode.WordWrap,
                Padding = new Thickness(12, 10),
                FontSize = 14,
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
            child.BackgroundColor = ThemeColors.Get(isSelected ? "PrimarySoft" : "Surface");
            child.BorderColor = ThemeColors.Get(isSelected ? "Primary" : "Stroke");
            child.BorderWidth = 1;
        }
        UpdateCheckState();
    }

    private void OnSentenceWordClick(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: VocabularyHint hint } || _displayedExercise is null)
            return;

        var exercise = _displayedExercise;
        try
        {
            var saved = _archive.Save(hint.Word, hint.Meaning, exercise.Sentence);
            peekWordLabel.Text = saved.Word;
            peekMeaningLabel.Text = saved.Meaning;
            wordPeekPanel.IsVisible = true;
        }
        catch { ShowStorageWarning("뜻: " + hint.Meaning + " · 보관함에 저장하지 못했습니다. 기존 기록은 유지됩니다."); }
    }

    private async void OnCheckClick(object? sender, EventArgs e)
    {
        if (_isGrading || _isLoading || _isEvaluating) return;
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
                checkButton.Text = "모든 단어를 배치하세요";
                return;
            }
            await GradeAsync(SentenceAnswer.Matches(string.Join(" ", _selectedTiles.Select(index => _tiles[index])), exercise.Sentence));
            return;
        }
        if (exercise.Kind == SentenceExerciseKind.ChooseMeaning)
        {
            if (string.IsNullOrWhiteSpace(_selectedChoice))
            {
                checkButton.Text = "답을 선택하세요";
                return;
            }

            await GradeAsync(_selectedChoice == exercise.Korean);
            return;
        }

        var input = answerEntry.Text ?? "";
        if (string.IsNullOrWhiteSpace(input))
        {
            answerHintLabel.Text = "답을 입력하세요.";
            answerHintLabel.TextColor = ThemeColors.Get("Danger");
            answerEntry.Focus();
            return;
        }

        if (exercise.Kind == SentenceExerciseKind.WriteSentence)
        {
            await EvaluateWritingAsync(exercise, input.Trim());
            return;
        }
        await GradeAsync(SentenceAnswer.Matches(input, exercise.TargetWord));
    }

    private void UpdateWritingConnectionState()
    {
        if (!_viewActive || _isEvaluating) return;
        if (!_chatGpt.PlanUsageEnabled)
        {
            writingStatusLabel.Text = _chatGpt.IsConnected
                ? "ChatGPT 구독 사용 권한이 필요합니다. 다시 연결해 권한을 승인해 주세요."
                : "Continue with ChatGPT로 본인 계정을 연결해 주세요.";
            writingConnectionButton.Text = "Continue with ChatGPT";
            UpdateCheckState();
            return;
        }
        writingConnectionButton.Text = "연결 관리";
        writingStatusLabel.Text = $"ChatGPT 플랜 사용 · {_chatGpt.AccountLabel}\nGPT-6.1 Sol로 문장의 의미와 문법을 확인합니다.";
        UpdateCheckState();
    }

    private async void OnWritingConnectionClick(object? sender, EventArgs e)
    {
        if (_isEvaluating || _isGrading) return;
        await Shell.Current.GoToAsync("//account");
    }

    private async void OnWritingUsageClick(object? sender, EventArgs e)
    {
        try { await Browser.Default.OpenAsync(new Uri("https://chatgpt.com/#settings/Usage"), BrowserLaunchMode.SystemPreferred); }
        catch { writingStatusLabel.Text = "브라우저를 열지 못했습니다. ChatGPT 설정에서 사용량을 확인해 주세요."; }
    }

    private async Task EvaluateWritingAsync(SentenceExercise exercise, string answer)
    {
        if (_isEvaluating || _remaining.Count == 0 || !_viewActive || _lessonAccount != _session.StorageId) return;
        if (!_chatGpt.PlanUsageEnabled)
        {
            writingStatusLabel.Text = "본인 ChatGPT 계정을 먼저 연결해 주세요. GPT-6.1 Sol을 사용합니다.";
            return;
        }
        using var cancellation = new CancellationTokenSource();
        _writingCancellation = cancellation;
        _isEvaluating = true;
        var owner = _session.StorageId;
        var token = _session.AccessToken;
        var connectionVersion = _chatGpt.ConnectionVersion;
        answerEntry.IsEnabled = hintButton.IsEnabled = lessonActions.IsEnabled = false;
        writingConnectionButton.IsEnabled = false;
        writingActivity.IsRunning = writingActivity.IsVisible = true;
        writingStatusLabel.Text = "문장의 의미와 문법을 확인하는 중…";
        UpdateCheckState();
        try
        {
            var result = await _writing.EvaluateAsync(exercise, answer, ChatGptWritingService.WritingModel, cancellation.Token);
            if (cancellation.IsCancellationRequested || !_viewActive || !_session.IsLoggedIn || owner != _session.StorageId
                || token != _session.AccessToken || connectionVersion != _chatGpt.ConnectionVersion
                || _remaining.Count == 0 || !ReferenceEquals(_remaining.Peek(), exercise)) return;
            if (!result.IsDecidable)
            {
                // No dequeue, XP, mistakes, progress or sample disclosure on an uncertain result.
                writingStatusLabel.Text = "판단 보류 · " + result.Feedback + "\n점수와 학습 기록은 변경하지 않았습니다.";
                return;
            }
            _isEvaluating = false;
            await GradeAsync(result.Accepted, writingEvaluation: result, submittedAnswer: answer);
        }
        catch (OperationCanceledException)
        {
            if (_viewActive && !cancellation.IsCancellationRequested)
                writingStatusLabel.Text = "판단 시간이 초과됐습니다. 답안은 유지되며 오답으로 기록하지 않았습니다. 다시 시도해 주세요.";
        }
        catch (Exception error)
        {
            if (_viewActive && owner == _session.StorageId)
                writingStatusLabel.Text = WritingErrorMessage(error) + "\n오답으로 기록하지 않았습니다.";
        }
        finally
        {
            if (ReferenceEquals(_writingCancellation, cancellation)) _writingCancellation = null;
            _isEvaluating = false;
            writingActivity.IsRunning = writingActivity.IsVisible = false;
            writingConnectionButton.IsEnabled = true;
            if (!_feedbackVisible)
            {
                answerEntry.IsEnabled = lessonActions.IsEnabled = true;
                hintButton.IsEnabled = !_assisted;
            }
            UpdateCheckState();
        }
    }

    private static string WritingErrorMessage(Exception error) => error is ChatGptException aiError
        ? aiError.SafeMessage
        : "ChatGPT에 연결하지 못했습니다. 연결 상태를 확인하고 다시 시도해 주세요.";

    private async void OnDontKnowClick(object? sender, EventArgs e)
    {
        if (_feedbackVisible || _remaining.Count == 0 || _isEvaluating || _isGrading)
            return;

        await GradeAsync(correct: false, revealed: true);
    }

    private async Task GradeAsync(bool correct, bool revealed = false,
        WritingEvaluation? writingEvaluation = null, string? submittedAnswer = null)
    {
        if (_remaining.Count == 0 || _feedbackVisible || _isGrading)
            return;

        if (_lessonAccount != _session.StorageId)
        {
            await DisplayAlertAsync("계정 변경됨", "현재 계정에서 학습을 다시 시작하세요.", "확인");
            await Shell.Current.GoToAsync("//home");
            return;
        }

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
            _mistakes.Add(exercise.Id);
            _remaining.Enqueue(exercise);
        }
        try { _review.Record(exercise.Id, correct && !_assisted); }
        catch { ShowStorageWarning("오답 기록을 저장하지 못했습니다. 이번 답안이 복습 목록에 반영되지 않을 수 있습니다."); }

        try
        {
            _streak.RegisterStudyToday();
            _sessionXp += _metrics.RegisterReview(correct);
        }
        catch { ShowStorageWarning("XP 또는 연속 학습 기록을 기기에 저장하지 못했습니다."); }
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

        var accent = ThemeColors.Get(correct ? "Success" : revealed ? "Warning" : "Danger");
        feedbackPanel.Stroke = accent;
        feedbackPanel.BackgroundColor = ThemeColors.Get(correct ? "SuccessSoft" : revealed ? "WarningSoft" : "DangerSoft");
        feedbackIconBorder.BackgroundColor = accent;
        feedbackIconLabel.Text = correct ? "✓" : "!";
        feedbackTitleLabel.Text = correct ? "정답" : revealed ? "정답 확인 · 다시 출제" : "오답 · 다시 출제";
        feedbackBodyLabel.Text = $"{exercise.Sentence}\n{exercise.Korean}\n{exercise.TargetWord} · {exercise.TargetMeaning}";
        writingFeedbackDetails.IsVisible = writingEvaluation is not null;
        if (writingEvaluation is not null)
        {
            feedbackTitleLabel.Text = writingEvaluation.Verdict switch
            {
                "accepted" => writingEvaluation.Corrections.Count == 0 ? "의미가 잘 전달됐어요" : "의미 통과 · 다듬기 제안",
                "revise" => "문장을 다듬어 보세요 · 다시 출제",
                _ => "뜻이 달라요 · 다시 출제"
            };
            feedbackBodyLabel.Text = writingEvaluation.Feedback;
            writingSubmittedLabel.Text = submittedAnswer;
            writingCorrectedLabel.Text = writingEvaluation.CorrectedAnswer;
            writingCorrectionPanel.IsVisible = !string.IsNullOrWhiteSpace(writingEvaluation.CorrectedAnswer);
            writingCorrectionsLabel.Text = string.Join("\n", writingEvaluation.Corrections.Select(correction =>
                $"{correction.Original} → {correction.Revised}\n{correction.Reason}"));
            writingCorrectionsLabel.IsVisible = writingEvaluation.Corrections.Count > 0;
            writingSuggestedLabel.Text = writingEvaluation.SuggestedAnswer;
        }
        checkButton.Text = "다음";
        sessionXpLabel.Text = $"+{_sessionXp} XP";
        lessonProgress.Progress = _lesson.Count == 0 ? 0 : (double)_completed.Count / _lesson.Count;
        try { await SaveServerProgressAsync(exercise, correct && !_assisted); }
        finally { _isGrading = false; UpdateCheckState(); }
        if (_viewActive) await lessonScroll.ScrollToAsync(feedbackPanel, ScrollToPosition.MakeVisible, true);
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
            if (word.Id.StartsWith("lexicore-v1:", StringComparison.Ordinal))
            {
                if (!syncStatusLabel.IsVisible)
                    ShowStorageWarning("기본 학습팩 진도는 현재 계정의 이 기기에만 저장됩니다. 기기 간 자동 동기화는 지원하지 않습니다.");
            }
        }
        catch
        {
            ShowStorageWarning(BuiltInWordProgressService.IsBuiltInId(word.Id)
                ? "기본팩 단어 진도를 저장하지 못했습니다. 단어장에서 기록을 확인하세요."
                : "서버 진도를 저장하지 못했습니다. 오답·코스 기록은 이 기기에만 남으며 자동 재전송되지 않습니다.");
        }
    }

    private void ShowStorageWarning(string message)
    {
        syncStatusLabel.Text = message;
        syncStatusLabel.IsVisible = true;
    }

    private void CompleteLesson()
    {
        lessonPanel.IsVisible = false;
        completionPanel.IsVisible = true;
        feedbackPanel.IsVisible = false;
        scoreLabel.Text = $"{_firstPassCorrect} / {_lesson.Count}";
        earnedXpLabel.Text = $"+{_sessionXp} XP";
        retryMistakesButton.IsVisible = _mistakes.Count > 0;
        reviewSummaryLabel.Text = _mistakes.Count == 0 ? "오답 없음" : $"이번 학습 오답 {_mistakes.Count}개";
        if (_stageId is not null && !_completionRecorded)
        {
            try
            {
                var stars = _course.Complete(_stageId, _firstPassCorrect, _completed.Count);
                completionStars.Text = new string('★', stars) + new string('☆', 3 - stars);
                completionTitle.Text = "단계 완료";
                completionMessage.Text = "별점을 저장했습니다.";
                nextLessonButton.Text = "코스 이어가기";
                _completionRecorded = true;
            }
            catch
            {
                completionStars.Text = "";
                completionTitle.Text = "학습 완료 · 기록 저장 실패";
                completionMessage.Text = "별점을 저장하지 못했습니다. 저장 공간을 확인한 뒤 다시 시도하세요.";
                nextLessonButton.Text = "코스로 돌아가기";
            }
        }
        else if (_stageId is null)
        {
            completionTitle.Text = "학습 완료";
            completionMessage.Text = "모든 문제를 풀었습니다.";
            completionStars.Text = "";
            nextLessonButton.Text = "새 학습 시작";
        }
        _ = lessonScroll.ScrollToAsync(0, 0, false);
    }

    private async void OnNewLessonClick(object? sender, EventArgs e)
    {
        if (_isGrading || _isEvaluating) return;
        if (_stageId is null) await StartNewLessonAsync();
        else await Shell.Current.GoToAsync("..");
    }

    private void OnRetryMistakesClick(object? sender, EventArgs e)
    {
        if (_isGrading || _isLoading || _isEvaluating) return;
        _lesson = _lesson.Where(exercise => _mistakes.Contains(exercise.Id)).ToList();
        if (_lesson.Count == 0) return;
        _stageId = null;
        lessonTitle.Text = "이번 오답 다시 풀기";
        lessonSubtitle.Text = $"{_lesson.Count}문제 · 기존 별점 유지";
        RestartLesson();
    }

    private async void OnCourseClick(object? sender, EventArgs e)
    {
        if (_isGrading || _isLoading) return;
        _writingCancellation?.Cancel();
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
        if (_displayedExercise is null || _feedbackVisible || _isGrading || _isEvaluating) return;
        _assisted = true;
        sentenceTokens.IsVisible = true;
        wordHelpLabel.IsVisible = true;
        BuildSentence(_displayedExercise, hideTarget: false);
        hintButton.IsEnabled = false;
        hintButton.Text = "힌트 사용됨";
        answerHintLabel.Text = "힌트를 사용한 답은 첫 시도 정답에서 제외됩니다.";
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
            selectedTokens.Children.Add(new Label { Text = "선택한 단어가 여기에 표시됩니다", FontSize = 13, TextColor = ThemeColors.Get("TextMuted"), Margin = 8 });
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
            SentenceExerciseKind.FillBlank => !string.IsNullOrWhiteSpace(answerEntry.Text),
            SentenceExerciseKind.WriteSentence => !string.IsNullOrWhiteSpace(answerEntry.Text)
                && _session.IsLoggedIn && _chatGpt.PlanUsageEnabled,
            _ => false
        });
        checkButton.IsEnabled = ready && !_isGrading && !_isEvaluating;
        checkButton.Opacity = checkButton.IsEnabled ? 1 : .45;
        checkButton.Text = _isEvaluating ? "판단 중…" : _feedbackVisible ? "다음"
            : _displayedExercise?.Kind == SentenceExerciseKind.WriteSentence ? "AI로 확인" : "정답 확인";
    }

    private void AddTile(FlexLayout container, int index, bool selected)
    {
        var used = !selected && _selectedTiles.Contains(index);
        var button = new Button
        {
            Text = _tiles[index], FontSize = 14, Margin = 3, Padding = new Thickness(10, 4),
            HeightRequest = DeviceInfo.Idiom == DeviceIdiom.Phone ? 44 : 36,
            BackgroundColor = ThemeColors.Get(selected ? "PrimarySoft" : "SurfaceElevated"),
            TextColor = used ? Colors.Transparent : ThemeColors.Get("TextPrimary"),
            IsEnabled = !used, Opacity = used ? .25 : 1,
            CornerRadius = 6, MinimumWidthRequest = DeviceInfo.Idiom == DeviceIdiom.Phone ? 44 : 36,
            BorderWidth = 1, BorderColor = ThemeColors.Get(selected ? "Primary" : "Stroke")
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
