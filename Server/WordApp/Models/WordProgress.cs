namespace WordApp.Models;

// Per-user learning progress for a word. Kept separate from Word.Status,
// which is content owned by Notion and overwritten on every sync.
public class WordProgress
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";   // User.UserId (login id)
    public string WordId { get; set; } = "";    // Word.Id (Notion page id)
    public string Status { get; set; } = "New"; // New | Learning | Mastered
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public DateTime? LastReviewed { get; set; }
    public DateTime UpdatedAt { get; set; }
}
