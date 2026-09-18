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
    public static T Get<T>(string key, T fallback) => Values.TryGetValue(key, out var value) ? (T)value : fallback;
    public static void Set<T>(string key, T value) where T : notnull => Values[key] = value;
    public static bool ContainsKey(string key) => Values.ContainsKey(key);
    public static void Remove(string key) => Values.Remove(key);
}
