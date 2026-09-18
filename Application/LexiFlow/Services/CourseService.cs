using System.Text.Json;
using LexiFlow.Models;

namespace LexiFlow.Services;

public sealed class CourseService(SessionService session, SentenceCatalogService catalog)
{
    private const int StageSize = 8;
    private string Key(string suffix) => LocalAccountData.Key(session, "course_" + suffix);

    public IReadOnlyList<CourseStage> GetStages(IEnumerable<Word>? words = null)
    {
        var stages = Read<List<CourseStage>>("stages") ?? [];
        if (words is null && stages.Count > 0)
            return stages;

        // Snapshot lessons so later content edits cannot move completed stage boundaries.
        var pool = catalog.GetCoursePool(words ?? []);
        var existing = stages.SelectMany(stage => stage.Exercises).Select(item => item.Id).ToHashSet();
        var additions = pool.Where(item => !existing.Contains(item.Id))
            .OrderBy(item => SentenceAnswer.Tokens(item.Sentence).Length)
            .ThenBy(item => item.Sentence.Length).ThenBy(item => item.Id, StringComparer.Ordinal)
            .Chunk(StageSize);
        foreach (var chunk in additions)
        {
            var number = stages.Count + 1;
            stages.Add(new CourseStage { Id = $"stage-{number}", Number = number, Exercises = chunk.ToList() });
        }
        Preferences.Set(Key("stages"), JsonSerializer.Serialize(stages));
        return stages;
    }

    public int Stars(string id) => (Read<Dictionary<string, int>>("stars") ?? []).GetValueOrDefault(id);

    public bool IsUnlocked(CourseStage stage, IReadOnlyList<CourseStage> stages)
    {
        var index = stages.ToList().FindIndex(item => item.Id == stage.Id);
        return index >= 0 && stages.Take(index).All(item => Stars(item.Id) > 0);
    }

    public IReadOnlyList<SentenceExercise> StartStage(string id)
    {
        var stages = GetStages();
        var stage = stages.FirstOrDefault(item => item.Id == id);
        if (stage is null || !IsUnlocked(stage, stages))
            return [];
        var kinds = stage.Unit == 0
            ? new[] { SentenceExerciseKind.ChooseMeaning, SentenceExerciseKind.ArrangeWords, SentenceExerciseKind.FillBlank }
            : new[] { SentenceExerciseKind.ArrangeWords, SentenceExerciseKind.FillBlank, SentenceExerciseKind.WriteSentence, SentenceExerciseKind.ChooseMeaning };
        return stage.Exercises.Select((item, index) => SentenceCatalogService.WithKind(item, kinds[index % kinds.Length])).ToList();
    }

    public int Complete(string id, int firstPassCorrect, int completedCount)
    {
        var stages = GetStages();
        var stage = stages.FirstOrDefault(item => item.Id == id);
        if (stage is null || !IsUnlocked(stage, stages) || completedCount != stage.Exercises.Count || completedCount == 0)
            return 0;
        var ratio = (double)Math.Clamp(firstPassCorrect, 0, completedCount) / completedCount;
        var stars = ratio >= .9 ? 3 : ratio >= .7 ? 2 : 1;
        var best = Read<Dictionary<string, int>>("stars") ?? [];
        best[id] = Math.Max(best.GetValueOrDefault(id), stars);
        Preferences.Set(Key("stars"), JsonSerializer.Serialize(best));
        return stars;
    }

    private T? Read<T>(string suffix)
    {
        try { return JsonSerializer.Deserialize<T>(Preferences.Get(Key(suffix), "null")); }
        catch (JsonException) { return default; }
    }
}
