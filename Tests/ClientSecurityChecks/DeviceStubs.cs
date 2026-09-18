namespace LexiFlow.Services;

// These adapters are entirely in memory; real MAUI user data is never touched.
public static class SecureStorage
{
    private static readonly Dictionary<string, string> Values = [];
    public static Task<string?> GetAsync(string key) => Task.FromResult(Values.GetValueOrDefault(key));
    public static Task SetAsync(string key, string value) { Values[key] = value; return Task.CompletedTask; }
    public static bool Remove(string key) => Values.Remove(key);
}

public static class Preferences
{
    private static readonly Dictionary<string, object> Values = [];
    public static T Get<T>(string key, T fallback) => Values.TryGetValue(key, out var value) ? (T)value : fallback;
    public static void Set<T>(string key, T value) where T : notnull => Values[key] = value;
    public static bool ContainsKey(string key) => Values.ContainsKey(key);
    public static void Remove(string key) => Values.Remove(key);
}
