namespace WordApp.Models;

public class Word
{
    public string Id { get; set; } = null!;      // Notion page ID (PK, upsert 기준)
    public string English { get; set; } = "";
    public string Meaning { get; set; } = "";
    public string Status { get; set; } = "";      // 외움/애매/모름/미분류
    public string? Note { get; set; }
    public string? Example { get; set; }
    public DateTime NotionCreated { get; set; }   // Notion Created 시각
    public string Source { get; set; } = "Manual"; // "Notion" | "Manual"
}