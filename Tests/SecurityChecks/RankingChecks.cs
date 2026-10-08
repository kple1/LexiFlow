using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WordApp.Data;
using WordApp.Models;
using WordApp.Services;

internal static partial class SecurityChecks
{
    private static async Task<RankingSnapshot> Ranking(HttpClient client)
    {
        using var response = await client.GetAsync("/ranking");
        response.EnsureSuccessStatusCode();
        Check(response.Headers.CacheControl?.NoStore == true, "ranking never enters shared cache");
        var json = await response.Content.ReadAsStringAsync();
        Check(!json.Contains("userId", StringComparison.OrdinalIgnoreCase) && !json.Contains("accountId", StringComparison.OrdinalIgnoreCase)
            && !json.Contains("accessToken", StringComparison.OrdinalIgnoreCase) && !json.Contains("email", StringComparison.OrdinalIgnoreCase),
            "ranking only exposes login IDs as display text, never numeric identity or authentication fields");
        return JsonSerializer.Deserialize<RankingSnapshot>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static async Task RankingChecks()
    {
        using (var factory = new ApiFactory())
        using (var client = factory.Client())
        {
            await Status(await client.GetAsync("/ranking"), HttpStatusCode.Unauthorized, "anonymous ranking denied");
            await Status(await client.PutAsJsonAsync("/ranking/me", new { participating = false }), HttpStatusCode.Unauthorized, "anonymous legacy changes denied");
            await Login(client);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.WordProgresses.Add(new() { UserId = "alice", WordId = "item", Status = "Mastered", CorrectCount = 10000 });
                db.GrammarProgresses.Add(new() { UserId = "alice", GrammarId = "item", Status = "Mastered" });
                db.IdiomProgresses.Add(new() { UserId = "alice", IdiomId = "item", Status = "Learning" });
                db.RankingProfiles.Add(new() { UserId = 1, Nickname = "Legacy Reader", NormalizedNickname = "LEGACY READER", JoinedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            var first = await Ranking(client);
            Check(first.ParticipantCount == 2 && first.Entries.Count == 2 && first.Me.Participating && first.Me.Rank == 1 && first.Me.Score == 2,
                "all accounts participate automatically without a nickname form");
            Check(first.Me.Nickname == "alice" && first.Entries.Select(e => e.Nickname).Order().SequenceEqual(new[] { "alice", "bob" }),
                "ranking displays actual login IDs without a nickname profile");
            Check(first.Entries.Any(e => e.Score == 0), "accounts with no progress are also included");
            var again = await Ranking(client);
            Check(first.Entries.Select(e => e.Nickname).SequenceEqual(again.Entries.Select(e => e.Nickname)), "login IDs remain stable across refreshes");
            await Status(await client.PutAsJsonAsync("/ranking/me", new { participating = false }), HttpStatusCode.Gone, "legacy client cannot withdraw automatic participation");
            await Status(await client.PutAsJsonAsync("/ranking/me", new { nickname = "alice", score = 99999 }), HttpStatusCode.Gone, "legacy client cannot set login ID or ranking score");
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Users.Add(new() { Id = 50, UserId = "reader.name.with.a.long.login.id", Pw = ApiFactory.AdminToken });
                await db.SaveChangesAsync();
            }
            var withNew = await Ranking(client);
            Check(withNew.ParticipantCount == 3 && withNew.Me.Score == 2 && withNew.Entries.Any(e => e.Nickname == "reader.name.with.a.long.login.id"), "new long dotted login IDs appear automatically without altering existing progress");
            using (var scope = factory.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Words.Where(w => w.Id == "item").ExecuteDeleteAsync();
            Check((await Ranking(client)).Me.Score == 1, "deleted content is excluded; repetition counters never inflate score");
            await Status(await client.SendAsync(new(HttpMethod.Delete, "/users/1") { Content = JsonContent.Create(new { CurrentPw = Password }) }), HttpStatusCode.NoContent, "automatic participant account deletion succeeds");
            using (var scope = factory.Services.CreateScope())
                Check(!await scope.ServiceProvider.GetRequiredService<AppDbContext>().RankingProfiles.AnyAsync(p => p.UserId == 1), "account deletion removes its preserved legacy profile");
            await Status(await client.GetAsync("/ranking"), HttpStatusCode.Unauthorized, "deleted account session denied");
        }

        using (var factory = new ApiFactory())
        using (var client = factory.Client())
        {
            await Login(client);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                for (var i = 0; i < 52; i++)
                {
                    var login = $"rank-test-{i}";
                    db.Users.Add(new() { Id = 100 + i, UserId = login, Pw = ApiFactory.AdminToken });
                    db.WordProgresses.Add(new() { UserId = login, WordId = "item", Status = "Mastered" });
                    if (i < 2) db.GrammarProgresses.Add(new() { UserId = login, GrammarId = "item", Status = "Mastered" });
                }
                await db.SaveChangesAsync();
            }
            var limited = await Ranking(client);
            Check(limited.ParticipantCount == 54 && limited.Entries.Count == 50 && limited.Me.Rank == 53 && limited.Entries.All(e => !e.IsMe),
                "top 50 is bounded and own rank remains available outside it");
            Check(limited.Entries[0].Rank == 1 && limited.Entries[1].Rank == 1 && limited.Entries[2].Rank == 3, "competition ties use 1, 1, 3");
            if (TestPostgres.Enabled)
            {
                using (var scope = factory.Services.CreateScope())
                    await scope.ServiceProvider.GetRequiredService<AppDbContext>().RankingProfiles.ExecuteDeleteAsync();
                var concurrent = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.GetAsync("/ranking")));
                foreach (var response in concurrent) { Check(response.StatusCode == HttpStatusCode.OK, "parallel reads safely rank all accounts without profiles"); response.Dispose(); }
                using var verify = factory.Services.CreateScope();
                Check(await verify.ServiceProvider.GetRequiredService<AppDbContext>().RankingProfiles.CountAsync() == 0, "ranking reads never create or mutate nickname profiles");
            }
        }
    }
}
