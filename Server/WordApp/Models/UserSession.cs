namespace WordApp.Models;

public class UserSession
{
    // Only the SHA-256 digest is persisted. The random bearer secret is returned once.
    public string TokenHash { get; set; } = "";
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string SecurityStamp { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}
