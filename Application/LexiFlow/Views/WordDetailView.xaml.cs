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
        exampleLabel.Text = string.IsNullOrWhiteSpace(word.Example) ? "-" : word.Example;

        bool hasNote = !string.IsNullOrWhiteSpace(word.Note);
        noteHeader.IsVisible = hasNote;
        noteLabel.IsVisible = hasNote;
        noteLabel.Text = word.Note ?? "";

        if (word.HasUserStatus)
        {
            statusBadge.IsVisible = true;
            statusLabel.Text = word.UserStatus;
            statusBadge.BackgroundColor = (Color)new StatusColorConverter()
                .Convert(word.UserStatus, typeof(Color), null, null!);
        }
    }

    private async void OnListenClick(object sender, EventArgs e)
    {
        try
        {
            var locales = await TextToSpeech.Default.GetLocalesAsync();
            var englishLocale = locales.FirstOrDefault(l => l.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase));

            await TextToSpeech.Default.SpeakAsync(_word.English, new SpeechOptions { Locale = englishLocale });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Speech unavailable", ex.Message, "OK");
        }
    }
}
