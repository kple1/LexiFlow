using System.Text.Json;
using System.Text.RegularExpressions;
using LexiFlow.Models;

namespace LexiFlow.Services;

// Original, versioned learning content. No network, API key or paid generation.
public static class BuiltInVocabulary
{
    private static readonly Lazy<IReadOnlyList<Entry>> Entries = new(Load);
    public static int Count => Entries.Value.Count;
    public static List<Word> GetWords() => Entries.Value.Select(ToWord).ToList();

    public static List<Word> Merge(IEnumerable<Word> serverWords)
    {
        // Prefer server identities so existing progress and archive links survive.
        var result = new List<Word>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builtIn = GetWords().ToDictionary(w => w.English, StringComparer.OrdinalIgnoreCase);
        foreach (var word in serverWords.Where(w => !string.IsNullOrWhiteSpace(w.English)))
        {
            var key = word.English.Trim();
            if (!seen.Add(key)) continue;
            if (builtIn.TryGetValue(key, out var supplement))
            {
                word.Topic = supplement.Topic;
                word.Level = supplement.Level;
                if (string.IsNullOrWhiteSpace(word.Meaning) || word.Meaning.Trim() == supplement.Meaning)
                    word.PartOfSpeech = supplement.PartOfSpeech;
                // Do not pair an existing, different sense with our example.
                if (string.IsNullOrWhiteSpace(word.Meaning))
                { word.Meaning = supplement.Meaning; word.Example = supplement.Example; }
            }
            result.Add(word);
        }
        result.AddRange(builtIn.Values.Where(w => seen.Add(w.English)));
        return result;
    }

    private static Word ToWord(Entry entry) => new()
    {
        Id = "lexicore-v1:" + entry.English.ToLowerInvariant().Replace(' ', '-'),
        English = entry.English, Meaning = entry.Meaning,
        Example = $"{entry.Sentence} ({entry.Translation})", Topic = entry.Topic,
        Level = entry.Level, PartOfSpeech = entry.PartOfSpeech,
        Note = "LexiFlow 기본 학습팩 · 난이도는 앱 내부 분류입니다."
    };

    private static IReadOnlyList<Entry> Load()
    {
        var result = new List<Entry>();
        foreach (var name in new[] { "foundation", "expansion" })
        {
            using var stream = typeof(BuiltInVocabulary).Assembly.GetManifestResourceStream($"LexiFlow.Content.vocabulary-{name}.json")
                ?? throw new InvalidOperationException("The bundled vocabulary resource is missing.");
            result.AddRange(JsonSerializer.Deserialize<List<Entry>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? []);
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in result)
            if (string.IsNullOrWhiteSpace(item.English) || !seen.Add(item.English)
                || string.IsNullOrWhiteSpace(item.Meaning) || string.IsNullOrWhiteSpace(item.Translation)
                || !Regex.IsMatch(item.Sentence, $@"(?<![\p{{L}}']){Regex.Escape(item.English)}(?![\p{{L}}'])", RegexOptions.IgnoreCase)
                || item.Level is not ("기초" or "일상" or "확장"))
                throw new InvalidOperationException("Invalid or duplicate bundled vocabulary entry.");
        return result;
    }

    private sealed record Entry(string English, string Meaning, string Sentence, string Translation, string Topic, string Level, string PartOfSpeech);
}
