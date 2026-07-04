using LexiFlow.Models;

namespace LexiFlow.Services;

// Chooses which items to drill next, favouring ones that are due for review.
// Pure logic (no I/O), generic over any item keyed by a string id, so both words
// and grammar points use the same spaced-repetition rules.
public static class ReviewScheduler
{
    // How long after a review an item of each status becomes due again.
    private static readonly Dictionary<string, TimeSpan> Intervals = new()
    {
        ["Learning"] = TimeSpan.FromDays(1),
        ["Mastered"] = TimeSpan.FromDays(7),
    };

    // Score for never-studied (or unknown-status) items so they are reviewed first.
    private const double NeverStudiedScore = 1000.0;

    public static List<T> SelectForReview<T>(
        IReadOnlyList<T> items,
        Func<T, string> idOf,
        IReadOnlyDictionary<string, ILearningProgress> progressById,
        int count,
        DateTime nowUtc)
    {
        return items
            .Select(x => (item: x, score: BaseScore(Lookup(progressById, idOf(x)), nowUtc) * Jitter()))
            .OrderByDescending(t => t.score)
            .Take(count)
            .Select(t => t.item)
            .ToList();
    }

    // Number of items currently due for review (interval elapsed, or never studied).
    public static int CountDue<T>(
        IReadOnlyList<T> items,
        Func<T, string> idOf,
        IReadOnlyDictionary<string, ILearningProgress> progressById,
        DateTime nowUtc)
    {
        return items.Count(x => BaseScore(Lookup(progressById, idOf(x)), nowUtc) >= 1.0);
    }

    private static ILearningProgress? Lookup(IReadOnlyDictionary<string, ILearningProgress> map, string id)
        => map.TryGetValue(id, out var p) ? p : null;

    // Higher = more overdue. >= 1 means the review interval has elapsed (i.e. due).
    private static double BaseScore(ILearningProgress? p, DateTime nowUtc)
    {
        if (p?.LastReviewed is null)
            return NeverStudiedScore;

        if (Intervals.TryGetValue(p.Status, out var interval) && interval > TimeSpan.Zero)
        {
            var elapsed = nowUtc - p.LastReviewed.Value;
            return elapsed.TotalSeconds / interval.TotalSeconds;
        }

        return NeverStudiedScore; // unknown status (e.g. "New") -> treat as due
    }

    // Jitter so the same items aren't drilled in the same order every session,
    // while strongly-overdue items still stay ahead of not-yet-due ones.
    private static double Jitter() => 0.85 + Random.Shared.NextDouble() * 0.30;
}
