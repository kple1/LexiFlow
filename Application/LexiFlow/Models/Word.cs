namespace LexiFlow.Models;

public class Word
{
    public string Id { get; set; } = "";
    public string English { get; set; } = "";
    public string Meaning { get; set; } = "";
    public string Status { get; set; } = "";
    public string Example { get; set; } = "";
    public string? Note { get; set; }
    public string Topic { get; set; } = "서버 단어";
    public string Level { get; set; } = "미분류";
    public string PartOfSpeech { get; set; } = "";
    public string CollectionLabel => $"{Level} · {Topic}";

    // Per-user progress badge, filled in client-side after loading progress (not sent by /words).
    public string? UserStatus { get; set; }
    public bool HasUserStatus => !string.IsNullOrEmpty(UserStatus);
}
