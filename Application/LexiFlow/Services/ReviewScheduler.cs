using LexiFlow.Models;

namespace LexiFlow.Services;

// Chooses which words to drill next, favouring words that are due for review.
// Pure logic (no I/O) so the selection can be reasoned about and tested on its own.
public static class ReviewScheduler
{
    // How long after a review a word of each status becomes due again.
    private static readonly Dictionary<string, TimeSpan> Intervals = new()
    {
        ["Learning"] = TimeSpan.FromDays(1),
        ["Mastered"] = TimeSpan.FromDays(7),
    };

    // Score given to never-studied (or unknown-status) words so they are reviewed first.
    private const double NeverStudiedScore = 1000.0;

    public static List<Word> SelectForReview(
        IReadOnlyList<Word> words,
        IReadOnlyDictionary<string, WordProgress> progressByWord,
        int count,
        DateTime nowUtc)
    {
        return words
            .Select(w => (word: w, score: Score(w, progressByWord, nowUtc)))
            .OrderByDescending(x => x.score)
            .Take(count)
            .Select(x => x.word)
            .ToList();
    }

    // Higher score = more overdue = picked sooner.
    private static double Score(Word word, IReadOnlyDictionary<string, WordProgress> map, DateTime nowUtc)
    {
        double baseScore;
        if (!map.TryGetValue(word.Id, out var p) || p.LastReviewed is null)
        {
            baseScore = NeverStudiedScore;
        }
        else if (Intervals.TryGetValue(p.Status, out var interval) && interval > TimeSpan.Zero)
        {
            // ratio >= 1 means the interval has elapsed and the word is due.
            var elapsed = nowUtc - p.LastReviewed.Value;
            baseScore = elapsed.TotalSeconds / interval.TotalSeconds;
        }
        else
        {
            baseScore = NeverStudiedScore; // unknown status (e.g. "New") -> treat as due
        }

        // Jitter so the same words aren't drilled in the same order every session,
        // while strongly-overdue words still stay ahead of not-yet-due ones.
        var jitter = 0.85 + Random.Shared.NextDouble() * 0.30;
        return baseScore * jitter;
    }
}
