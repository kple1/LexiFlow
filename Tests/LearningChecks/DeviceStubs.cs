// In-memory adapter: tests never touch the learner's MAUI preferences or account.
namespace LexiFlow.Services;

public sealed class SessionService
{
    public string? CurrentUserId { get; set; }
    public string StorageId => CurrentUserId ?? "guest";
}

public static class Preferences
{
    private static readonly Dictionary<string, object> Values = [];
    public static int RemainingWritesBeforeFailure { get; set; } = -1;
    public static IReadOnlyCollection<string> Keys => Values.Keys;
    public static T Get<T>(string key, T fallback) => Values.TryGetValue(key, out var value) ? (T)value : fallback;
    public static void Set<T>(string key, T value) where T : notnull
    {
        if (RemainingWritesBeforeFailure == 0) throw new IOException("Simulated interrupted preference write.");
        if (RemainingWritesBeforeFailure > 0) RemainingWritesBeforeFailure--;
        if (value is string text && System.Text.Encoding.Unicode.GetByteCount(text) > 8192)
            throw new IOException("Windows preference value exceeds 8 KiB.");
        Values[key] = value;
    }
    public static bool ContainsKey(string key) => Values.ContainsKey(key);
    public static void Remove(string key) => Values.Remove(key);
}
