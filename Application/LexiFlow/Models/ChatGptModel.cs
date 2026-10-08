namespace LexiFlow.Models;

public sealed class ChatGptModel
{
    public string Slug { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public override string ToString() => DisplayName;
}
