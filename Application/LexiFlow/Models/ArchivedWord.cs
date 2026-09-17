namespace LexiFlow.Models;

public sealed class ArchivedWord
{
    public string Word { get; set; } = "";
    public string Meaning { get; set; } = "";
    public string Sentence { get; set; } = "";
    public DateTimeOffset SavedAt { get; set; }
    public int Lookups { get; set; }
}
