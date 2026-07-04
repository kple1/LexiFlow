namespace WordApp.Models;

// Per-user learning progress for an idiom/phrasal verb. Parallel to GrammarProgress;
// kept separate from Idiom.Status, which is admin-owned content.
public class IdiomProgress
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";     // User.UserId (login id)
    public string IdiomId { get; set; } = "";     // Idiom.Id
    public string Status { get; set; } = "New";   // New | Learning | Mastered
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public DateTime? LastReviewed { get; set; }
    public DateTime UpdatedAt { get; set; }
}
