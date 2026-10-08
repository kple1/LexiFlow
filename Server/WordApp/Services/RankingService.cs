using Microsoft.EntityFrameworkCore;
using WordApp.Data;

namespace WordApp.Services;

public sealed record RankingEntry(int Rank, string Nickname, int MasteredWords, int MasteredGrammar,
    int MasteredIdioms, int Score, bool IsMe);
public sealed record RankingMe(bool Participating, string Nickname, int? Rank, int MasteredWords,
    int MasteredGrammar, int MasteredIdioms, int Score);
public sealed record RankingSnapshot(DateTime GeneratedAt, int ParticipantCount,
    IReadOnlyList<RankingEntry> Entries, RankingMe Me);

public sealed class RankingService(AppDbContext db)
{
    public async Task<RankingSnapshot> ReadAsync(int accountId, CancellationToken token)
    {
        // A stable read snapshot prevents concurrent account/progress changes from
        // producing contradictory participant counts, top entries or own rank.
        await using var transaction = await db.Database.BeginTransactionAsync(
            db.Database.IsNpgsql() ? System.Data.IsolationLevel.RepeatableRead : System.Data.IsolationLevel.Serializable, token);
        var scores = Scores();
        var participants = await scores.CountAsync(token);
        var top = await scores.OrderByDescending(s => s.Words + s.Grammar + s.Idioms)
            .ThenBy(s => s.AccountId).Take(50).ToListAsync(token);
        var entries = new List<RankingEntry>();
        var previous = -1;
        var rank = 0;
        for (var i = 0; i < top.Count; i++)
        {
            var row = top[i];
            var score = row.Words + row.Grammar + row.Idioms;
            if (score != previous) rank = i + 1; // Competition ranking: 1, 1, 3.
            previous = score;
            entries.Add(new(rank, row.Nickname, row.Words, row.Grammar, row.Idioms, score, row.AccountId == accountId));
        }
        var own = await scores.SingleOrDefaultAsync(row => row.AccountId == accountId, token);
        if (own is null) throw new KeyNotFoundException("The account no longer exists.");
        var ownScore = own.Words + own.Grammar + own.Idioms;
        var ownRank = 1 + await scores.CountAsync(row => row.Words + row.Grammar + row.Idioms > ownScore, token);
        var me = new RankingMe(true, own.Nickname, ownRank, own.Words, own.Grammar, own.Idioms, ownScore);
        await transaction.CommitAsync(token);
        return new(DateTime.UtcNow, participants, entries, me);
    }

    // Explicit product policy: login IDs are displayed to authenticated readers.
    // Nickname remains the wire field name for compatibility with older clients.
    private IQueryable<ScoreRow> Scores() => db.Users.AsNoTracking().Select(user => new ScoreRow
    {
        AccountId = user.Id, Nickname = user.UserId,
        Words = db.WordProgresses.Count(p => p.UserId == user.UserId && p.Status == "Mastered" && db.Words.Any(w => w.Id == p.WordId)),
        Grammar = db.GrammarProgresses.Count(p => p.UserId == user.UserId && p.Status == "Mastered" && db.Grammars.Any(g => g.Id == p.GrammarId)),
        Idioms = db.IdiomProgresses.Count(p => p.UserId == user.UserId && p.Status == "Mastered" && db.Idioms.Any(i => i.Id == p.IdiomId))
    });

    private sealed class ScoreRow
    {
        public int AccountId { get; init; }
        public string Nickname { get; init; } = "";
        public int Words { get; init; }
        public int Grammar { get; init; }
        public int Idioms { get; init; }
    }
}
