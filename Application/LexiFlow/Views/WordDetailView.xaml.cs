using LexiFlow.Models;
using LexiFlow.Services;

namespace LexiFlow.Views;

public partial class WordDetailView : ContentPage
{
    private readonly Word _word;
    private readonly ArchiveService _archive;
    private readonly SessionService _session;
    private readonly string _openedForAccount;
    private readonly string? _openedForToken;
    private readonly string _exampleSentence;
    private CancellationTokenSource? _speechCancellation;

    public WordDetailView(Word word, ArchiveService archive, SessionService session)
    {
        InitializeComponent();
        _word = word;
        _archive = archive;
        _session = session;
        _openedForAccount = session.StorageId;
        _openedForToken = session.AccessToken;

        englishLabel.Text = word.English;
        meaningLabel.Text = word.Meaning;
        collectionLabel.Text = word.CollectionLabel;
        partOfSpeechLabel.Text = word.PartOfSpeech;
        progressStorageLabel.Text = BuiltInWordProgressService.IsBuiltInId(word.Id)
            ? "이 기기에 저장" : "계정에 저장";
        partOfSpeechLabel.IsVisible = !string.IsNullOrWhiteSpace(word.PartOfSpeech);
        var example = WordLibraryQuery.SplitExample(word.Example);
        _exampleSentence = example.Sentence;
        exampleLabel.Text = string.IsNullOrWhiteSpace(example.Sentence) ? "아직 등록된 예문이 없어요." : example.Sentence;
        translationLabel.Text = example.Translation;
        translationLabel.IsVisible = translationDivider.IsVisible = !string.IsNullOrWhiteSpace(example.Translation);
        exampleListenButton.IsVisible = !string.IsNullOrWhiteSpace(_exampleSentence);
        noteCard.IsVisible = !string.IsNullOrWhiteSpace(word.Note);
        noteLabel.Text = word.Note ?? "";
        UpdateAccountState();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _session.StateChanged -= OnSessionChanged;
        _session.StateChanged += OnSessionChanged;
        UpdateAccountState();
    }

    protected override void OnDisappearing()
    {
        _session.StateChanged -= OnSessionChanged;
        _speechCancellation?.Cancel();
        base.OnDisappearing();
    }

    private void OnSessionChanged(object? sender, EventArgs e)
        => MainThread.BeginInvokeOnMainThread(UpdateAccountState);

    private void UpdateAccountState()
    {
        var sameAccount = _openedForAccount == _session.StorageId && _openedForToken == _session.AccessToken;
        statusLabel.Text = sameAccount ? WordLibraryQuery.StatusLabel(_word.UserStatus) : "학습 기록 미확인";
        bool saved;
        try { saved = _archive.GetAll().Any(item => item.Word.Equals(_word.English.Trim(), StringComparison.OrdinalIgnoreCase)); }
        catch
        {
            archiveButton.IsEnabled = false;
            archiveStatusLabel.Text = "기기에 저장된 Archive를 읽지 못했어요. 기존 내용을 덮어쓰지 않도록 저장을 중단했습니다.";
            return;
        }
        archiveButton.IsEnabled = !saved;
        archiveButton.Text = saved ? "Archive에 저장됨" : "Archive에 저장";
        archiveStatusLabel.Text = saved
            ? _session.IsLoggedIn
                ? "이 기기의 현재 계정 Archive에 저장되어 있어요. 단어와 예문을 함께 복습할 수 있습니다."
                : "이 기기의 게스트 Archive에 저장되어 있어요. 계정 간 자동 이동은 되지 않아요."
            : _session.IsLoggedIn
                ? "단어와 예문을 이 기기의 현재 계정 Archive에 저장합니다."
                : "로그인 전에는 이 기기의 게스트 Archive에 저장됩니다. 계정 간 자동 이동은 되지 않아요.";
    }

    private void OnSaveArchiveClick(object? sender, EventArgs e)
    {
        try
        {
            _archive.Save(_word.English, _word.Meaning, _exampleSentence);
            UpdateAccountState();
        }
        catch { archiveStatusLabel.Text = "이 기기에 저장하지 못했어요. 잠시 후 다시 시도해 주세요."; }
    }

    private async void OnOpenArchiveClick(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//archive");

    private async void OnListenClick(object? sender, EventArgs e) => await SpeakAsync(_word.English);
    private async void OnListenExampleClick(object? sender, EventArgs e) => await SpeakAsync(_exampleSentence);

    private async Task SpeakAsync(string text)
    {
        if (_speechCancellation is not null || string.IsNullOrWhiteSpace(text)) return;
        using var cancellation = new CancellationTokenSource();
        _speechCancellation = cancellation;
        speechErrorBorder.IsVisible = false;
        wordListenButton.IsEnabled = exampleListenButton.IsEnabled = false;
        try
        {
            var locales = await TextToSpeech.Default.GetLocalesAsync();
            cancellation.Token.ThrowIfCancellationRequested();
            var englishLocale = locales.FirstOrDefault(locale => locale.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase));
            if (englishLocale is null)
            {
                speechErrorLabel.Text = "기기에 영어 음성이 설치되어 있지 않아요. 운영체제의 음성 언어 설정에서 영어를 추가해 주세요.";
                speechErrorBorder.IsVisible = true;
                return;
            }
            await TextToSpeech.Default.SpeakAsync(text, new SpeechOptions { Locale = englishLocale }, cancellation.Token);
        }
        catch (OperationCanceledException) { }
        catch
        {
            speechErrorLabel.Text = "이 기기에서는 현재 발음을 재생할 수 없어요. 음성 설정을 확인해 주세요.";
            speechErrorBorder.IsVisible = true;
        }
        finally
        {
            _speechCancellation = null;
            wordListenButton.IsEnabled = exampleListenButton.IsEnabled = true;
        }
    }
}
