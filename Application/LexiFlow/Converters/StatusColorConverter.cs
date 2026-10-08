using System.Globalization;
using LexiFlow.Services;

namespace LexiFlow.Converters;

// Maps a per-user word status to a badge colour.
public class StatusColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value as string) switch
        {
            "Mastered" => ThemeColors.Get("SuccessSoft"),
            "Learning" => ThemeColors.Get("WarningSoft"),
            "New" => ThemeColors.Get("SurfaceElevated"),
            _ => Colors.Transparent
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
