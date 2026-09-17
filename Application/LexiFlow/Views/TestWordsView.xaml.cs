using System.Text.RegularExpressions;
using LexiFlow.Models;
using LexiFlow.Services;

namespace LexiFlow.Views;

public partial class TestWordsView : ContentPage
{
    private static readonly Regex TokenPattern = new(@"[\p{L}']+|[^\p{L}\s]", RegexOptions.Compiled);
    private readonly ApiService _api;
    private readonly SessionService _session;
    private readonly StreakService _streak;
    private readonly LearningMetricsService _metrics;
    private readonly ArchiveService _archive;
    private readonly SentenceCatalogService _catalog;
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

    public TestWordsView(
        ApiService api,
        SessionService session,
        StreakService streak,
        LearningMetricsService metrics,
        ArchiveService archive,
        SentenceCatalogService catalog)
    {
        InitializeComponent();
        _api = api;
        _session = session;
        _streak = streak;
        _metrics = metrics;
        _archive = archive;
        _catalog = catalog;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_lesson.Count == 0 && !_isLoading)
            await StartNewLessonAsync();
    }

    private async Task StartNewLessonAsync()
    {
        _isLoading = true;
        loadingOverlay.IsVisible = true;
        lessonPanel.IsVisible = false;
        completionPanel.IsVisible = false;

        _lesson = _catalog.CreateLesson(10).ToList();
        _serverWords.Clear();

        try
        {
            var words = await _api.GetWordsAsync();
            foreach (var word in words.Where(word => !string.IsNullOrWhiteSpace(word.English)))
                _serverWords[NormalizeWord(word.English)] = word;
        }
        catch
        {
            // The built-in sentence course remains fully usable offline.
        }

        RestartLesson();
        loadingOverlay.IsVisible = false;
        _isLoading = false;
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
        _selectedChoice = null;
        _feedbackVisible = false;
        wordPeekPanel.IsVisible = false;
        feedbackPanel.IsVisible = false;
        lessonActions.IsVisible = true;
        checkButton.Text = "정답 확인";
        checkButton.IsEnabled = true;
        sessionXpLabel.Text = $"+{_sessionXp} XP";
        lessonProgress.Progress = _lesson.Count == 0 ? 0 : (double)_completed.Count / _lesson.Count;
        counterLabel.Text = $"{Math.Min(_completed.Count + 1, _lesson.Count)} / {_lesson.Count}";

        var isChoice = exercise.Kind == SentenceExerciseKind.ChooseMeaning;
        typeLabel.Text = isChoice ? "문장 뜻 고르기" : "빈칸 직접 쓰기";
        instructionLabel.Text = isChoice ? "이 문장의 뜻을 고르세요" : "빈칸에 들어갈 영어 단어를 쓰세요";
        koreanPromptBorder.IsVisible = !isChoice;
        koreanPromptLabel.Text = exercise.Korean;
        choicePanel.IsVisible = isChoice;
        writePanel.IsVisible = !isChoice;
        answerEntry.Text = "";
        answerEntry.IsEnabled = true;
        answerHintLabel.Text = "철자와 문맥을 함께 떠올려 보세요.";
        answerHintLabel.TextColor = Color.FromArgb("#A7B2C7");

        BuildSentence(exercise, hideTarget: !isChoice);
        BuildChoices(exercise);

        if (!isChoice)
            answerEntry.Focus();
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
                HeightRequest = 58,
                Padding = new Thickness(16, 0),
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
            child.BackgroundColor = Color.FromArgb(isSelected ? "#20345F" : "#1C2942");
            child.BorderColor = Color.FromArgb(isSelected ? "#5B8CFF" : "#2A3A58");
        }
    }

    private void OnSentenceWordClick(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: VocabularyHint hint } || _remaining.Count == 0)
            return;

        var exercise = _remaining.Peek();
        var saved = _archive.Save(hint.Word, hint.Meaning, exercise.Sentence);
        peekWordLabel.Text = saved.Word;
        peekMeaningLabel.Text = saved.Meaning;
        wordPeekPanel.IsVisible = true;
    }

    private async void OnCheckClick(object? sender, EventArgs e)
    {
        if (_feedbackVisible)
        {
            ShowCurrentExercise();
            return;
        }

        if (_remaining.Count == 0)
            return;

        var exercise = _remaining.Peek();
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

        await GradeAsync(NormalizeAnswer(input) == NormalizeAnswer(exercise.TargetWord));
    }

    private async void OnEntryCompleted(object? sender, EventArgs e)
    {
        if (_feedbackVisible)
            ShowCurrentExercise();
        else
            await CheckCurrentEntryAsync();
    }

    private async Task CheckCurrentEntryAsync()
    {
        if (_remaining.Count == 0)
            return;

        var input = answerEntry.Text ?? "";
        if (string.IsNullOrWhiteSpace(input))
            return;

        await GradeAsync(NormalizeAnswer(input) == NormalizeAnswer(_remaining.Peek().TargetWord));
    }

    private async void OnDontKnowClick(object? sender, EventArgs e)
    {
        if (_feedbackVisible || _remaining.Count == 0)
            return;

        await GradeAsync(correct: false, revealed: true);
    }

    private async Task GradeAsync(bool correct, bool revealed = false)
    {
        if (_remaining.Count == 0 || _feedbackVisible)
            return;

        var exercise = _remaining.Dequeue();
        var firstEncounter = _seen.Add(exercise.Id);

        if (correct)
        {
            _completed.Add(exercise.Id);
            if (firstEncounter)
                _firstPassCorrect++;
        }
        else
        {
            _remaining.Enqueue(exercise);
        }

        _streak.RegisterStudyToday();
        _sessionXp += _metrics.RegisterReview(correct);
        await SaveServerProgressAsync(exercise, correct);

        _feedbackVisible = true;
        feedbackPanel.IsVisible = true;
        lessonActions.IsVisible = false;
        answerEntry.IsEnabled = false;
        foreach (var button in choicePanel.Children.OfType<Button>())
            button.IsEnabled = false;

        var accent = Color.FromArgb(correct ? "#22C55E" : "#F59E0B");
        feedbackPanel.Stroke = accent;
        feedbackPanel.BackgroundColor = Color.FromArgb(correct ? "#163A2A" : "#3B2B15");
        feedbackIconBorder.BackgroundColor = accent;
        feedbackIconLabel.Text = correct ? "✓" : "!";
        feedbackTitleLabel.Text = correct ? "정답이에요!" : revealed ? "정답을 확인했어요" : "괜찮아요, 뒤에서 다시 만나요";
        feedbackBodyLabel.Text = exercise.Kind == SentenceExerciseKind.FillBlank
            ? $"{exercise.TargetWord} · {exercise.TargetMeaning}\n{exercise.Sentence}"
            : exercise.Korean;
        checkButton.Text = "계속";
        sessionXpLabel.Text = $"+{_sessionXp} XP";
        lessonProgress.Progress = _lesson.Count == 0 ? 0 : (double)_completed.Count / _lesson.Count;
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
                correct ? "Mastered" : "Learning");
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
    }

    private void OnResetClick(object? sender, EventArgs e) => RestartLesson();

    private async void OnNewLessonClick(object? sender, EventArgs e)
        => await StartNewLessonAsync();

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

    private static string NormalizeAnswer(string value)
        => Regex.Replace(value.Trim().ToLowerInvariant(), @"[^\p{L}\p{N}']", "");
}
