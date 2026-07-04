namespace LexiFlow.Models;

// Per-user learning progress for a word (mirrors the server's WordProgress).
public class WordProgress
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string WordId { get; set; } = "";
    public string Status { get; set; } = "";   // New | Learning | Mastered
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public DateTime? LastReviewed { get; set; }
    public DateTime UpdatedAt { get; set; }
}
