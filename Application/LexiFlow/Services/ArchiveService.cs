using System.Text.Json;
using LexiFlow.Models;

namespace LexiFlow.Services;

/// <summary>
/// Stores words the learner tapped inside a sentence. Archives are scoped to the
/// signed-in user and remain available offline on the device.
/// </summary>
public sealed class ArchiveService
{
    private const int MaximumEntries = 500;
    private readonly SessionService _session;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public ArchiveService(SessionService session)
    {
        _session = session;
    }

    public IReadOnlyList<ArchivedWord> GetAll()
        => Load()
            .OrderByDescending(item => item.SavedAt)
            .ToList();

    public ArchivedWord Save(string word, string meaning, string sentence)
    {
        var normalizedWord = word.Trim().ToLowerInvariant();
        var entries = Load();
        var existing = entries.FirstOrDefault(item =>
            item.Word.Equals(normalizedWord, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            existing = new ArchivedWord
            {
                Word = normalizedWord,
                Meaning = meaning.Trim(),
                Sentence = sentence.Trim(),
                SavedAt = DateTimeOffset.Now,
                Lookups = 1
            };
            entries.Add(existing);
        }
        else
        {
            existing.Meaning = meaning.Trim();
            existing.Sentence = sentence.Trim();
            existing.SavedAt = DateTimeOffset.Now;
            existing.Lookups++;
        }

        entries = entries
            .OrderByDescending(item => item.SavedAt)
            .Take(MaximumEntries)
            .ToList();
        SaveAll(entries);
        return existing;
    }

    public void Remove(string word)
    {
        var entries = Load();
        entries.RemoveAll(item => item.Word.Equals(word, StringComparison.OrdinalIgnoreCase));
        SaveAll(entries);
    }

    private List<ArchivedWord> Load()
    {
        try
        {
            var json = Preferences.Get(StorageKey(), "[]");
            return JsonSerializer.Deserialize<List<ArchivedWord>>(json, _jsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void SaveAll(List<ArchivedWord> entries)
        => Preferences.Set(StorageKey(), JsonSerializer.Serialize(entries, _jsonOptions));

    private string StorageKey()
    {
        var user = string.IsNullOrWhiteSpace(_session.CurrentUserId)
            ? "guest"
            : _session.CurrentUserId.Trim().ToLowerInvariant();
        return $"sentence_archive_{user}_v1";
    }
}
