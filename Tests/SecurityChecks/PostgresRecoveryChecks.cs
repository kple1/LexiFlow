using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using WordApp.Auth;
using WordApp.Data;
using WordApp.Models;
using WordApp.Services;

internal static partial class SecurityChecks
{
    private static async Task PostgresRecoveryChecks()
    {
        await MigrationPreservesAccounts();
        await ConcurrentReset(false);
        await ConcurrentReset(true);
        await ConcurrentSignup();
        await ConcurrentEmailBinding(false);
        await ConcurrentEmailBinding(true);
        await ConcurrentPasswordChangeAndReset();
        await ConcurrentMailBudget();
    }

    private static async Task MigrationPreservesAccounts()
    {
        using var fixture = new TestPostgres();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.ConnectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync("20260918021632_EnforceProgressOwnership");
        var hash = BCrypt.Net.BCrypt.HashPassword(Password, 11);
        var stamp = Guid.NewGuid().ToString("N");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Users" ("Id", "UserId", "Pw", "SecurityStamp", "FailedLoginCount")
            VALUES (42, 'legacy', {hash}, {stamp}, 0)
            """);
        var sessionHash = SessionAuthenticationHandler.HashToken("test-only-migration-session");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "UserSessions" ("TokenHash", "UserId", "SecurityStamp", "ExpiresAt")
            VALUES ({sessionHash}, 42, {stamp}, {DateTime.UtcNow.AddDays(1)})
            """);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "WordProgresses" ("UserId", "WordId", "Status", "CorrectCount", "WrongCount", "UpdatedAt")
            VALUES ('legacy', 'synthetic-word', 'Learning', 7, 2, now())
            """);
        await db.Database.MigrateAsync();
        var user = await db.Users.SingleAsync();
        Check(user.Id == 42 && user.Pw == hash && user.SecurityStamp == stamp && user.VerifiedEmail is null,
            "PostgreSQL upgrade preserves existing ID, password, stamp and nullable recovery email");
        Check(await db.UserSessions.CountAsync() == 1 && (await db.WordProgresses.SingleAsync()).CorrectCount == 7,
            "PostgreSQL upgrade preserves device session and learning progress");
        Check(!(await db.Database.GetPendingMigrationsAsync()).Any(), "PostgreSQL upgrade applies complete migration chain");
        await db.Database.MigrateAsync();
        Check(await db.Users.CountAsync() == 1, "PostgreSQL migration rerun is a no-op");
    }

    private static async Task<string[]> SeedProofs(ApiFactory factory, string purpose, int count, bool sameUser = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == 1);
        var tokens = Enumerable.Range(0, count).Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(32))).ToArray();
        for (var i = 0; i < count; i++)
        {
            var owner = purpose == AccountEmailWorkflow.Signup ? null : sameUser ? user : await db.Users.AsNoTracking().SingleAsync(u => u.Id == i + 1);
            db.AccountActionTokens.Add(new AccountActionToken
            {
                TokenHash = SessionAuthenticationHandler.HashToken(tokens[i]), Purpose = purpose,
                UserId = owner?.Id, SecurityStamp = owner?.SecurityStamp,
                Email = "race@example.test", NormalizedEmail = "RACE@EXAMPLE.TEST", ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            });
        }
        await db.SaveChangesAsync();
        return tokens;
    }

    private static async Task ConcurrentReset(bool distinctTokens)
    {
        using var factory = new ApiFactory(true);
        using var client = factory.Client();
        await SetEmail(factory, email: "race@example.test");
        await Login(client);
        using var anonymous = factory.Client();
        var proofs = await SeedProofs(factory, AccountEmailWorkflow.Reset, distinctTokens ? 4 : 1);
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => anonymous.PostAsJsonAsync("/account/password/reset",
            new { Token = proofs[distinctTokens ? i : 0], Pw = "Race-password-2026" })));
        try
        {
            Check(responses.Count(r => r.StatusCode == HttpStatusCode.OK) == 1
                && responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest) == 3,
                $"PostgreSQL parallel {(distinctTokens ? "different" : "same")} reset proofs produce one success and three safe rejections (no deadlocks)");
        }
        finally { foreach (var response in responses) response.Dispose(); }
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Check(!await db.AccountActionTokens.AnyAsync() && !await db.UserSessions.AnyAsync(), "parallel reset revokes every old proof and device session");
    }

    private static async Task ConcurrentSignup()
    {
        using var factory = new ApiFactory(true);
        using var client = factory.Client();
        var proofs = await SeedProofs(factory, AccountEmailWorkflow.Signup, 2);
        var responses = await Task.WhenAll(proofs.Select((token, index) => client.PostAsJsonAsync("/account/signup/complete",
            new { Token = token, UserId = "race_user_" + index, Pw = Password })));
        try
        {
            Check(responses.Count(r => r.StatusCode == HttpStatusCode.OK) == 1
                && responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest) == 1,
                "PostgreSQL two signup links for same email create exactly one account without deadlock");
        }
        finally { foreach (var response in responses) response.Dispose(); }
        using var scope = factory.Services.CreateScope();
        Check(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync(u => u.NormalizedEmail == "RACE@EXAMPLE.TEST") == 1,
            "PostgreSQL email uniqueness holds across concurrent signup");
    }

    private static async Task ConcurrentEmailBinding(bool sameUser)
    {
        using var factory = new ApiFactory(true);
        using var client = factory.Client();
        var proofs = await SeedProofs(factory, AccountEmailWorkflow.Bind, 2, sameUser);
        var responses = await Task.WhenAll(proofs.Select(token => client.PostAsJsonAsync("/account/email/confirm", new { Token = token })));
        try
        {
            Check(responses.Count(r => r.StatusCode == HttpStatusCode.OK) == 1
                && responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest) == 1,
                $"PostgreSQL simultaneous email claims ({(sameUser ? "same account" : "different accounts")}) produce one success and one safe rejection");
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    private static async Task ConcurrentPasswordChangeAndReset()
    {
        using var factory = new ApiFactory(true);
        using var client = factory.Client();
        using var anonymous = factory.Client();
        await SetEmail(factory, email: "race@example.test");
        await Login(client);
        var proofs = await SeedProofs(factory, AccountEmailWorkflow.Reset, 1);
        var responses = await Task.WhenAll(
            client.PatchAsJsonAsync("/users/1", new { CurrentPw = Password, Pw = "Device-password-2026" }),
            anonymous.PostAsJsonAsync("/account/password/reset", new { Token = proofs[0], Pw = "Reset-password-2026" }));
        try
        {
            Check(responses.Count(r => r.IsSuccessStatusCode) == 1 && responses.All(r => r.IsSuccessStatusCode
                || r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden),
                "PostgreSQL concurrent in-app password change and recovery cannot deadlock or both succeed");
        }
        finally { foreach (var response in responses) response.Dispose(); }
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Check(!await db.AccountActionTokens.AnyAsync() && !await db.UserSessions.AnyAsync(),
            "in-app/recovery race leaves no pre-change proof or session");
    }

    private static async Task ConcurrentMailBudget()
    {
        using var factory = new ApiFactory(true);
        using var client = factory.Client();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Issue(factory, AccountEmailWorkflow.Signup, "budget@example.test")));
        Check(factory.Mail.Messages.Count == 1, "PostgreSQL concurrent first budget inserts allow only one recipient attempt");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Check(await db.AccountMailBudgets.Where(b => b.Key.StartsWith("global-")).AllAsync(b => b.Count == 1),
                "PostgreSQL rejected concurrent mail attempts roll back global counters");
            await db.AccountMailBudgets.Where(b => b.Key.StartsWith("global-day:")).ExecuteUpdateAsync(u => u.SetProperty(b => b.Count, 49));
        }
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Issue(factory, AccountEmailWorkflow.Signup, $"limit{i}@example.test")));
        Check(factory.Mail.Messages.Count == 2, "PostgreSQL simultaneous different recipients cannot exceed the daily global cap");
        using var final = factory.Services.CreateScope();
        Check(await final.ServiceProvider.GetRequiredService<AppDbContext>().AccountMailBudgets
            .Where(b => b.Key.StartsWith("global-day:")).Select(b => b.Count).SingleAsync() == 50,
            "PostgreSQL global attempt counter stops at 50");
    }
}
