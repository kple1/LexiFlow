namespace WordApp.Models;

// Per-user learning progress for a grammar point. Parallel to WordProgress;
// kept separate from Grammar.Status, which is Notion-owned content.
public class GrammarProgress
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";     // User.UserId (login id)
    public string GrammarId { get; set; } = "";   // Grammar.Id (Notion page id)
    public string Status { get; set; } = "New";   // New | Learning | Mastered
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public DateTime? LastReviewed { get; set; }
    public DateTime UpdatedAt { get; set; }
}
