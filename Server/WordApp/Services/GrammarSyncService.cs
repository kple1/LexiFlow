using Microsoft.EntityFrameworkCore;
using WordApp.Data;

namespace WordApp.Services
{

    public class GrammarSyncService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly NotionService _notion;

        public GrammarSyncService(IServiceProvider sp, NotionService notion)
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
                    var fromNotion = await _notion.FetchAllAsyncGrammar(ct);

                    using var scope = _sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var existing = await db.Grammars.ToDictionaryAsync(g => g.Id, ct);

                    foreach (var w in fromNotion)
                    {
                        if (existing.TryGetValue(w.Id, out var cur))
                        {
                            cur.Title = w.Title;
                            cur.Category = w.Category;
                            cur.Example = w.Example;
                            cur.Explanation = w.Explanation;
                            cur.Note = w.Note;
                            cur.Status = w.Status;
                        }
                        else db.Grammars.Add(w);
                    }

                    var notionIds = fromNotion.Select(w => w.Id).ToHashSet();
                    db.Grammars.RemoveRange(existing.Values.Where(x => !notionIds.Contains(x.Id)));

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
}