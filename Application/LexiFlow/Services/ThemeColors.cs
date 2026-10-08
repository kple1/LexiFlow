namespace LexiFlow.Services;

// Dynamic exercise controls use the same semantic palette as the XAML views.
public static class ThemeColors
{
    public static Color Get(string key)
    {
        var resources = Application.Current?.Resources
            ?? throw new InvalidOperationException("Application resources are not initialized.");
        return resources.TryGetValue(key, out var resource) && resource is Color color
            ? color
            : throw new InvalidOperationException($"The theme color '{key}' is missing.");
    }
}
