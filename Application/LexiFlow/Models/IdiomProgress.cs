namespace LexiFlow.Models;

// Per-user learning progress for an idiom/phrasal verb (mirrors the server's IdiomProgress).
public class IdiomProgress : ILearningProgress
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string IdiomId { get; set; } = "";
    public string Status { get; set; } = "";   // New | Learning | Mastered
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public DateTime? LastReviewed { get; set; }
    public DateTime UpdatedAt { get; set; }
}
