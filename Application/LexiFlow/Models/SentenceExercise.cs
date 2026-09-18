namespace LexiFlow.Models;

public enum SentenceExerciseKind
{
    ChooseMeaning,
    FillBlank,
    ArrangeWords,
    WriteSentence
}

public sealed class SentenceExercise
{
    public string Id { get; init; } = "";
    public SentenceExerciseKind Kind { get; init; }
    public string Sentence { get; init; } = "";
    public string Korean { get; init; } = "";
    public string TargetWord { get; init; } = "";
    public string TargetMeaning { get; init; } = "";
    public IReadOnlyList<string> Choices { get; init; } = [];
    public IReadOnlyList<VocabularyHint> Vocabulary { get; init; } = [];
}

public sealed class VocabularyHint
{
    public string Word { get; init; } = "";
    public string Meaning { get; init; } = "";
}
