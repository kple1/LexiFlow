namespace LexiFlow.Models;

// Common shape the ReviewScheduler needs, shared by WordProgress and GrammarProgress.
public interface ILearningProgress
{
    string Status { get; }
    DateTime? LastReviewed { get; }
}
