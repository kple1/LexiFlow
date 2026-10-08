using System.Net;
using System.Text.Json;
using LexiFlow.Models;

namespace LexiFlow.Services;

/// <summary>
/// Progress for bundled identities is device-local, never sent to an older server.
/// Storage/read errors deliberately propagate: failed persistence is not a successful review.
/// </summary>
public sealed class BuiltInWordProgressService(SessionService session)
{
    public const string StorageSuffix = "builtin_word_progress_v1";
    private static readonly object StorageGate = new();
    private static readonly Lazy<HashSet<string>> KnownIds = new(() =>
        BuiltInVocabulary.GetWords().Select(word => word.Id).ToHashSet(StringComparer.Ordinal));
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool IsBuiltInId(string? wordId)
        => wordId?.StartsWith("lexicore-v1:", StringComparison.Ordinal) == true;

    // Each Windows Preferences value is limited to 8 KB. Keep one small record per word.
    public static string StorageSuffixFor(string wordId) => $"{StorageSuffix}:{wordId}";

    public IReadOnlyList<WordProgress> Read(string userId)
    {
        lock (StorageGate)
        {
            var prefix = RequireOwner(userId);
            var token = session.AccessToken;
            var records = new List<WordProgress>();
            foreach (var wordId in KnownIds.Value)
            {
                var row = ReadStored($"{prefix}:{wordId}", userId, wordId);
                if (row is not null) records.Add(row);
            }
            if (RequireOwner(userId) != prefix || session.AccessToken != token)
                throw new InvalidOperationException("The signed-in account changed.");
            return records;
        }
    }

    public WordProgress Record(string userId, string wordId, bool correct, string? status = null, DateTime? nowUtc = null)
    {
        if (!KnownIds.Value.Contains(wordId))
            throw new ArgumentException("Unknown bundled learning item.", nameof(wordId));
        if (status is not null && status is not ("New" or "Learning" or "Mastered"))
            throw new ArgumentException("Invalid progress status.", nameof(status));
        lock (StorageGate)
        {
            var prefix = RequireOwner(userId);
            var token = session.AccessToken;
            var key = $"{prefix}:{wordId}";
            var row = ReadStored(key, userId, wordId);
            if (row is null)
            {
                row = new WordProgress { UserId = userId, WordId = wordId, Status = status ?? "Learning" };
            }
            else if (status is not null) row.Status = status;

            if (correct) row.CorrectCount = checked(row.CorrectCount + 1);
            else row.WrongCount = checked(row.WrongCount + 1);
            var now = nowUtc ?? DateTime.UtcNow;
            row.LastReviewed = row.UpdatedAt = now;
            // Capture and validate the same owner immediately before the only side effect.
            if (RequireOwner(userId) != prefix || session.AccessToken != token)
                throw new InvalidOperationException("The signed-in account changed.");
            Preferences.Set(key, JsonSerializer.Serialize(row, JsonOptions));
            return row;
        }
    }

    private string RequireOwner(string userId)
    {
        if (!string.Equals(userId, session.CurrentUserId, StringComparison.Ordinal))
            throw new InvalidOperationException("The requested account is not signed in.");
        if (!session.IsLoggedIn || session.AccessToken is null || session.CurrentAccountId is not > 0)
            throw new HttpRequestException("Please sign in again.", null, HttpStatusCode.Unauthorized);
        return LocalAccountData.Key(session, StorageSuffix);
    }

    private static WordProgress? ReadStored(string key, string userId, string wordId)
    {
        if (!Preferences.ContainsKey(key)) return null;
        var json = Preferences.Get(key, "");
        var row = JsonSerializer.Deserialize<WordProgress>(json, JsonOptions)
            ?? throw new InvalidDataException("Bundled progress storage is invalid.");
        if (row.UserId != userId || row.WordId != wordId ||
            row.Status is not ("New" or "Learning" or "Mastered") || row.CorrectCount < 0 || row.WrongCount < 0)
            throw new InvalidDataException("Bundled progress storage is invalid.");
        return row;
    }
}
