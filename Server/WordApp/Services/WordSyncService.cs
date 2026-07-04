using Microsoft.EntityFrameworkCore;
using WordApp.Data;

namespace WordApp.Services;

public class WordSyncService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly NotionService _notion;

    public WordSyncService(IServiceProvider sp, NotionService notion)
    {
        _sp = sp;
        _notion = notion;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var fromNotion = await _notion.FetchAllAsync(ct);
                foreach (var w in fromNotion)
                    w.Source = "Notion";

                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // 관리자 패널로 넣은 Manual 단어는 동기화 대상에서 완전히 제외
                var existing = await db.Words
                    .Where(w => w.Source == "Notion")
                    .ToDictionaryAsync(w => w.Id, ct);

                foreach (var w in fromNotion)
                {
                    if (existing.TryGetValue(w.Id, out var cur))
                    {
                        cur.English = w.English;
                        cur.Meaning = w.Meaning;
                        cur.Status = w.Status;
                        cur.Note = w.Note;
                    }
                    else db.Words.Add(w);
                }

                // Notion에서 삭제된 단어 제거 (Notion 출처만 대상)
                var notionIds = fromNotion.Select(w => w.Id).ToHashSet();
                db.Words.RemoveRange(existing.Values.Where(w => !notionIds.Contains(w.Id)));

                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Sync] {ex}");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }
    }
}