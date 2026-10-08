using LexiFlow.Models;

namespace LexiFlow.Services;

/// <summary>Local-only library queries. Filtering never fetches or changes account data.</summary>
public static class WordLibraryQuery
{
    public const string AllLevels = "모든 난이도";
    public const string AllTopics = "모든 주제";
    public const string AllStatuses = "전체 상태";
    public const string Alphabetical = "알파벳 A → Z";
    public const string ReverseAlphabetical = "알파벳 Z → A";

    public static string StatusLabel(string? status) => status switch
    {
        "Mastered" => "익힌 단어",
        "Learning" => "학습 중",
        null or "" or "New" => "새 단어",
        _ => "미확인"
    };

    public static IReadOnlyList<Word> Filter(
        IEnumerable<Word> source, string? search = null, string? level = null,
        string? topic = null, string? status = null, string? sort = null)
    {
        var terms = (search ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var filtered = source.Where(word =>
            (string.IsNullOrEmpty(level) || level == AllLevels || word.Level == level) &&
            (string.IsNullOrEmpty(topic) || topic == AllTopics || word.Topic == topic) &&
            (string.IsNullOrEmpty(status) || status == AllStatuses || StatusLabel(word.UserStatus) == status) &&
            terms.All(term => Contains(word.English, term) || Contains(word.Meaning, term) ||
                Contains(word.Example, term) || Contains(word.Topic, term)));
        return (sort == ReverseAlphabetical
                ? filtered.OrderByDescending(word => word.English, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderBy(word => word.English, StringComparer.OrdinalIgnoreCase))
            .ThenBy(word => word.Id, StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<string> TopicOptions(IEnumerable<Word> source)
        => new[] { AllTopics }.Concat(source.Select(word => word.Topic)
            .Where(topic => !string.IsNullOrWhiteSpace(topic))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)).ToList();

    public static (string Sentence, string Translation) SplitExample(string? example)
    {
        if (string.IsNullOrWhiteSpace(example)) return ("", "");
        var lines = example.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Support both authored formats, leaving ordinary English parentheses untouched.
        if (lines.Length > 1 && lines.Skip(1).Any(line => line.Any(c => c is >= '\uac00' and <= '\ud7a3')))
            return (lines[0], string.Join("\n", lines.Skip(1)));
        var text = example.Trim();
        var translationStart = text.LastIndexOf(" (", StringComparison.Ordinal);
        if (translationStart > 0 && text.EndsWith(')'))
        {
            var translation = text[(translationStart + 2)..^1];
            if (translation.Any(c => c is >= '\uac00' and <= '\ud7a3') && !translation.Contains('(') && !translation.Contains(')'))
                return (text[..translationStart].TrimEnd(), translation);
        }
        return (text, "");
    }

    private static bool Contains(string? value, string term)
        => value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
}
