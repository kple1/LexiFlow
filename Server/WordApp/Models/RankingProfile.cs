namespace WordApp.Models;

// Legacy display identities retained for backwards-compatible schema/rollback.
// Ranking 1.5.2 reads Users.UserId directly and never creates these profiles.
public sealed class RankingProfile
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Nickname { get; set; } = "";
    public string NormalizedNickname { get; set; } = "";
    public DateTime JoinedAt { get; set; }
}
