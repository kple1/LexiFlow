namespace LexiFlow.Models;

public class Word
{
    public string Id { get; set; } = "";
    public string English { get; set; } = "";
    public string Meaning { get; set; } = "";
    public string Status { get; set; } = "";
    public string Example { get; set; } = "";
    public string? Note { get; set; }

    // Per-user progress badge, filled in client-side after loading progress (not sent by /words).
    public string? UserStatus { get; set; }
    public bool HasUserStatus => !string.IsNullOrEmpty(UserStatus);
}