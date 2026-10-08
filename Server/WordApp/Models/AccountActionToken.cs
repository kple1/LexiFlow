namespace WordApp.Models;

// Only the SHA-256 digest is persisted. Mail links keep the bearer in a URL fragment.
public class AccountActionToken
{
    public string TokenHash { get; set; } = "";
    public string Purpose { get; set; } = "";
    public int? UserId { get; set; }
    public User? User { get; set; }
    public string? SecurityStamp { get; set; }
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}

// Persistent, atomic mail-attempt budgets survive app restarts. Keys contain hashes,
// not addresses. Expired counters are pruned by the mail worker.
public class AccountMailBudget
{
    public string Key { get; set; } = "";
    public int Count { get; set; }
    public DateTime ExpiresAt { get; set; }
}
