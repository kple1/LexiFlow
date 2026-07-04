namespace LexiFlow.Models;

// Per-user learning progress for a grammar point (mirrors the server's GrammarProgress).
public class GrammarProgress : ILearningProgress
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string GrammarId { get; set; } = "";
    public string Status { get; set; } = "";   // New | Learning | Mastered
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public DateTime? LastReviewed { get; set; }
    public DateTime UpdatedAt { get; set; }
}
