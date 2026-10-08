using System.Text.Json;

namespace LexiFlow.Services;

// Device-local review memory is explicitly separate from server progress.
public sealed class SentenceReviewService(SessionService session)
{
    public sealed class Memory
    {
        public int Attempts { get; set; }
        public int Wrong { get; set; }
        public int ConsecutiveCorrect { get; set; }
        public DateTime DueAt { get; set; }
        public DateTime LastAttempt { get; set; }
        public bool NeedsPractice => Wrong > 0 && ConsecutiveCorrect < 2;
    }

    public Dictionary<string, Memory> Read()
    {
        return JsonSerializer.Deserialize<Dictionary<string, Memory>>(LargePreferenceStore.Get(Key, "{}")) ?? [];
    }

    public void Record(string id, bool correct, DateTime? utcNow = null)
    {
        var current = utcNow ?? DateTime.UtcNow;
        var all = Read();
        var memory = all.GetValueOrDefault(id) ?? new Memory();
        memory.Attempts++;
        memory.LastAttempt = current;
        if (correct) memory.ConsecutiveCorrect++;
        else { memory.Wrong++; memory.ConsecutiveCorrect = 0; }
        memory.DueAt = correct
            ? current.AddDays(new[] { 1, 3, 7, 14, 30 }[Math.Min(memory.ConsecutiveCorrect - 1, 4)])
            : current.AddMinutes(10);
        all[id] = memory;
        LargePreferenceStore.Set(Key, JsonSerializer.Serialize(all));
    }

    private string Key => LocalAccountData.Key(session, "sentence_review");
}
