using LexiFlow.Converters;
using LexiFlow.Models;

namespace LexiFlow.Views;

public partial class WordDetailView : ContentPage
{
    private readonly Word _word;

    public WordDetailView(Word word)
    {
        InitializeComponent();
        _word = word;

        englishLabel.Text = word.English;
        meaningLabel.Text = word.Meaning;
        exampleLabel.Text = string.IsNullOrWhiteSpace(word.Example)
            ? "아직 등록된 예문이 없어요."
            : word.Example;

        noteCard.IsVisible = !string.IsNullOrWhiteSpace(word.Note);
        noteLabel.Text = word.Note ?? "";

        if (word.HasUserStatus)
        {
            statusBadge.IsVisible = true;
            statusLabel.Text = word.UserStatus;
            statusBadge.BackgroundColor = (Color)new StatusColorConverter()
                .Convert(word.UserStatus, typeof(Color), null, null!);
        }
    }

    private async void OnListenClick(object? sender, EventArgs e)
    {
        speechErrorBorder.IsVisible = false;

        try
        {
            var locales = await TextToSpeech.Default.GetLocalesAsync();
            var englishLocale = locales.FirstOrDefault(locale =>
                locale.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase));

            await TextToSpeech.Default.SpeakAsync(
                _word.English,
                new SpeechOptions { Locale = englishLocale });
        }
        catch
        {
            speechErrorLabel.Text = "이 기기에서는 현재 발음을 재생할 수 없어요.";
            speechErrorBorder.IsVisible = true;
        }
    }
}
