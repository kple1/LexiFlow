using System.Globalization;

namespace LexiFlow.Converters;

// Maps a per-user word status to a badge colour.
public class StatusColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value as string) switch
        {
            "Mastered" => Color.FromArgb("#2E7D32"), // green
            "Learning" => Color.FromArgb("#F9A825"), // amber
            "New" => Color.FromArgb("#555555"),      // grey
            _ => Colors.Transparent
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
