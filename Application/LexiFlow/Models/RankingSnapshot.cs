using System.Text.Json;
using System.Text.Json.Serialization;

namespace LexiFlow.Models;

public sealed class RankingSnapshot
{
    [JsonRequired] public DateTime GeneratedAt { get; init; }
    [JsonRequired] public int ParticipantCount { get; init; }
    [JsonRequired] public List<RankingEntry> Entries { get; init; } = [];
    [JsonRequired] public RankingMe Me { get; init; } = new();

    public void Validate()
    {
        if (ParticipantCount < 0 || GeneratedAt == default || Entries is null || Entries.Count > 50
            || Entries.Count > ParticipantCount || Me is null || !Me.Participating) throw new JsonException("Invalid automatic ranking snapshot.");
        var previous = int.MaxValue;
        var rank = 0;
        var ownCount = 0;
        for (var i = 0; i < Entries.Count; i++)
        {
            var entry = Entries[i] ?? throw new JsonException("Missing ranking entry.");
            CheckScore(entry.Score, entry.MasteredWords, entry.MasteredGrammar, entry.MasteredIdioms);
            if (entry.Score > previous) throw new JsonException("Invalid ranking order.");
            if (i == 0 || entry.Score != previous) rank = i + 1;
            if (entry.Rank != rank || !ValidDisplayId(entry.Nickname)) throw new JsonException("Invalid ranking entry.");
            if (entry.IsMe) ownCount++;
            previous = entry.Score;
        }
        CheckScore(Me.Score, Me.MasteredWords, Me.MasteredGrammar, Me.MasteredIdioms);
        if (Me.Participating)
        {
            if (Me.Rank is null || Me.Rank < 1 || Me.Rank > ParticipantCount || !ValidDisplayId(Me.Nickname))
                throw new JsonException("Invalid own ranking.");
            var own = Entries.FirstOrDefault(row => row.IsMe);
            if (ownCount > 1 || (own is not null && (own.Score != Me.Score || own.Rank != Me.Rank || own.Nickname != Me.Nickname)))
                throw new JsonException("Conflicting own ranking.");
        }
        else if (ownCount != 0 || Me.Rank is not null || Me.Nickname != "") throw new JsonException("Unpublished account cannot appear in ranking.");
    }

    private static void CheckScore(int score, int words, int grammar, int idioms)
    {
        if (words < 0 || grammar < 0 || idioms < 0 || score < 0 || score != (long)words + grammar + idioms)
            throw new JsonException("Invalid ranking score.");
    }
    // Matches the storage limit, including legacy IDs and dots. Rendered as text,
    // never markup; control characters are not valid display identities.
    private static bool ValidDisplayId(string? value) => value is { Length: >= 1 and <= 100 }
        && !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);
}

public sealed class RankingEntry
{
    [JsonRequired] public int Rank { get; init; }
    [JsonRequired] public string Nickname { get; init; } = "";
    [JsonRequired] public int MasteredWords { get; init; }
    [JsonRequired] public int MasteredGrammar { get; init; }
    [JsonRequired] public int MasteredIdioms { get; init; }
    [JsonRequired] public int Score { get; init; }
    [JsonRequired] public bool IsMe { get; init; }
    [JsonIgnore] public string RankLabel => $"{Rank:N0}위";
    [JsonIgnore] public bool IsChampion => Rank == 1;
    [JsonIgnore] public string Details => $"단어 {MasteredWords:N0} · 문법 {MasteredGrammar:N0} · 표현 {MasteredIdioms:N0}";
    [JsonIgnore] public string ScoreLabel => $"{Score:N0}개";
    [JsonIgnore] public string DisplayNickname => IsMe ? $"{Nickname} · 나" : Nickname;
}

public sealed class RankingMe
{
    [JsonRequired] public bool Participating { get; init; }
    [JsonRequired] public string Nickname { get; init; } = "";
    [JsonRequired] public int? Rank { get; init; }
    [JsonRequired] public int MasteredWords { get; init; }
    [JsonRequired] public int MasteredGrammar { get; init; }
    [JsonRequired] public int MasteredIdioms { get; init; }
    [JsonRequired] public int Score { get; init; }
}
