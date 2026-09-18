using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using WordApp.Auth;
using WordApp.Data;
using WordApp.Models;

internal static class SecurityChecks
{
    private const string Password = "Correct-password-2026";
    private static int _checks;
    public static async Task Main()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_WORDAPP",
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Server/WordApp")));
        await Ownership();
        await Sessions();
        await PasswordChange();
        await AccountDeletion();
        await CredentialValidation();
        await LoginLockout();
        await TransportAndLimits();
        Console.WriteLine($"{_checks} security checks passed (isolated SQLite; no production traffic).");
    }

    private static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception("FAIL " + name);
        Console.WriteLine("PASS " + name);
        _checks++;
    }

    private static async Task<Credentials> Login(HttpClient client, string user = "alice", string password = Password)
    {
        using var response = await client.PostAsJsonAsync("/users/login", new { UserId = user, Pw = password });
        response.EnsureSuccessStatusCode();
        var value = (await response.Content.ReadFromJsonAsync<Credentials>())!;
        Check(value.AccessToken.Length == 64 && value.ExpiresAt > DateTime.UtcNow, "login returns an expiring random token");
        client.DefaultRequestHeaders.Authorization = new("Bearer", value.AccessToken);
        return value;
    }

    private static async Task Status(HttpResponseMessage response, HttpStatusCode expected, string name)
    {
        using (response)
        {
            if (response.StatusCode != expected)
                throw new Exception($"FAIL {name}: expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            Check(true, name);
        }
    }

    private static async Task Ownership()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        foreach (var path in new[] { "/users/1", "/users/me", "/users/alice/progress", "/users/alice/grammar-progress", "/users/alice/idiom-progress" })
            await Status(await client.GetAsync(path), HttpStatusCode.Unauthorized, "anonymous read denied " + path);
        await Status(await client.PatchAsJsonAsync("/users/1", new { CurrentPw = Password, Pw = "New-password-2026" }), HttpStatusCode.Unauthorized, "anonymous password change denied");
        await Status(await client.SendAsync(new(HttpMethod.Delete, "/users/1") { Content = JsonContent.Create(new { CurrentPw = Password }) }), HttpStatusCode.Unauthorized, "anonymous deletion denied");
        foreach (var suffix in new[] { "progress", "grammar-progress", "idiom-progress" })
            await Status(await client.PostAsJsonAsync($"/users/alice/{suffix}", new { }), HttpStatusCode.Unauthorized, "anonymous write denied " + suffix);
        await Login(client);
        await Status(await client.GetAsync("/users/1"), HttpStatusCode.OK, "own account readable");
        await Status(await client.GetAsync("/users/2"), HttpStatusCode.Forbidden, "other account unreadable");
        foreach (var (suffix, field) in new[] { ("progress", "WordId"), ("grammar-progress", "GrammarId"), ("idiom-progress", "IdiomId") })
        {
            var body = new Dictionary<string, object> { [field] = "item", ["Correct"] = true, ["Status"] = "Learning" };
            await Status(await client.PostAsJsonAsync($"/users/alice/{suffix}", body), HttpStatusCode.OK, "own progress writable " + suffix);
            await Status(await client.GetAsync($"/users/alice/{suffix}"), HttpStatusCode.OK, "own progress readable " + suffix);
            await Status(await client.GetAsync($"/users/bob/{suffix}"), HttpStatusCode.Forbidden, "cross-account read denied " + suffix);
            await Status(await client.PostAsJsonAsync($"/users/bob/{suffix}", body), HttpStatusCode.Forbidden, "cross-account write denied " + suffix);
        }
        await Status(await client.GetAsync("/users/ALICE/progress"), HttpStatusCode.Forbidden, "case variant cannot access another identity");
        await Status(await client.PatchAsJsonAsync("/users/2", new { CurrentPw = Password, Pw = "New-password-2026" }), HttpStatusCode.Forbidden, "cross-account password change denied");
        await Status(await client.SendAsync(new(HttpMethod.Delete, "/users/2") { Content = JsonContent.Create(new { CurrentPw = Password }) }), HttpStatusCode.Forbidden, "cross-account deletion denied");
        await Status(await client.GetAsync("/admin/api/words"), HttpStatusCode.Unauthorized, "member token is not an admin credential");
        client.DefaultRequestHeaders.Add("X-Admin-Token", ApiFactory.AdminToken);
        await Status(await client.GetAsync("/admin/api/words"), HttpStatusCode.OK, "separate admin credential works");
    }

    private static async Task Sessions()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        var credentials = await Login(client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.UserSessions.SingleAsync();
            Check(row.TokenHash != credentials.AccessToken && row.TokenHash == SessionAuthenticationHandler.HashToken(credentials.AccessToken), "database stores digest, not bearer secret");
        }
        await Status(await client.PostAsync("/users/logout", null), HttpStatusCode.NoContent, "logout revokes server session");
        await Status(await client.GetAsync("/users/me"), HttpStatusCode.Unauthorized, "revoked session cannot be replayed");
        credentials = await Login(client);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserSessions.ExecuteUpdateAsync(u => u.SetProperty(s => s.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        await Status(await client.GetAsync("/users/me"), HttpStatusCode.Unauthorized, "expired session denied");
        client.DefaultRequestHeaders.Authorization = new("Bearer", new string('A', 64));
        await Status(await client.GetAsync("/users/me"), HttpStatusCode.Unauthorized, "forged session denied");
        client.DefaultRequestHeaders.Authorization = new("Bearer", "alice");
        await Status(await client.GetAsync("/users/me"), HttpStatusCode.Unauthorized, "legacy user id cannot authenticate");
    }

    private static async Task PasswordChange()
    {
        using var factory = new ApiFactory();
        using var first = factory.Client();
        using var second = factory.Client();
        await Login(first);
        await Login(second);
        await Status(await first.PatchAsJsonAsync("/users/1", new { CurrentPw = "wrong", Pw = "New-password-2026" }), HttpStatusCode.Forbidden, "password change requires current password");
        await Status(await first.PatchAsJsonAsync("/users/1", new { CurrentPw = Password, Pw = "short" }), HttpStatusCode.BadRequest, "weak replacement password rejected");
        await Status(await first.PatchAsJsonAsync("/users/1", new { CurrentPw = Password, Pw = "New-password-2026" }), HttpStatusCode.NoContent, "own password changes with verification");
        await Status(await first.GetAsync("/users/me"), HttpStatusCode.Unauthorized, "password change revokes current session");
        await Status(await second.GetAsync("/users/me"), HttpStatusCode.Unauthorized, "password change revokes other devices");
        await Status(await first.PostAsJsonAsync("/users/login", new { UserId = "alice", Pw = Password }), HttpStatusCode.Unauthorized, "old password no longer works");
        await Login(first, password: "New-password-2026");
    }

    private static async Task AccountDeletion()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        await Login(client);
        await client.PostAsJsonAsync("/users/alice/progress", new { WordId = "item", Correct = true });
        await client.PostAsJsonAsync("/users/alice/grammar-progress", new { GrammarId = "item", Correct = true });
        await client.PostAsJsonAsync("/users/alice/idiom-progress", new { IdiomId = "item", Correct = true });
        await Status(await client.SendAsync(new(HttpMethod.Delete, "/users/1") { Content = JsonContent.Create(new { CurrentPw = "wrong" }) }), HttpStatusCode.Forbidden, "deletion requires current password");
        await Status(await client.SendAsync(new(HttpMethod.Delete, "/users/1") { Content = JsonContent.Create(new { CurrentPw = Password }) }), HttpStatusCode.NoContent, "verified account deletion succeeds");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Check(!await db.Users.AnyAsync(u => u.Id == 1) && !await db.UserSessions.AnyAsync()
            && !await db.WordProgresses.AnyAsync() && !await db.GrammarProgresses.AnyAsync() && !await db.IdiomProgresses.AnyAsync(), "account, sessions and three kinds of progress removed");
        Check(await db.Users.AnyAsync(u => u.Id == 2), "other account survives deletion");
        await Status(await client.GetAsync("/users/me"), HttpStatusCode.Unauthorized, "deleted account token denied");
    }

    private static async Task CredentialValidation()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        await Status(await client.PostAsJsonAsync("/users", new { UserId = "new_user", Pw = "short" }), HttpStatusCode.BadRequest, "weak signup rejected");
        await Status(await client.PostAsJsonAsync("/users", new { UserId = "new_user", Pw = new string('가', 30) }), HttpStatusCode.BadRequest, "BCrypt byte limit enforced for Unicode");
        await Status(await client.PostAsJsonAsync("/users", new { UserId = "../bad", Pw = Password }), HttpStatusCode.BadRequest, "unsafe new user id rejected");
        await Status(await client.PostAsJsonAsync("/users", new { UserId = "new_user\n", Pw = Password }), HttpStatusCode.BadRequest, "trailing newline in user id rejected");
        await Status(await client.PostAsJsonAsync("/users", new { UserId = "new_user", Pw = Password }), HttpStatusCode.Created, "valid signup succeeds");
        await Status(await client.PostAsJsonAsync("/users", new { UserId = "new_user", Pw = Password }), HttpStatusCode.Conflict, "duplicate account rejected");
        await Status(await client.PostAsJsonAsync("/users/login", new { UserId = "alice", Pw = (string?)null }), HttpStatusCode.BadRequest, "null password rejected");
    }

    private static async Task LoginLockout()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        for (var i = 0; i < 5; i++)
            await Status(await client.PostAsJsonAsync("/users/login", new { UserId = "alice", Pw = "incorrect" }), HttpStatusCode.Unauthorized, "failed login is generic");
        await Status(await client.PostAsJsonAsync("/users/login", new { UserId = "alice", Pw = Password }), HttpStatusCode.Unauthorized, "account locked after five failures");
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.Id == 1)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.LockoutUntil, DateTime.UtcNow.AddMinutes(-1)));
        await Login(client);
    }

    private static async Task TransportAndLimits()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        using var cleartext = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        cleartext.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        await Status(await cleartext.PostAsJsonAsync("/users/login", new { UserId = "alice", Pw = Password }), HttpStatusCode.BadRequest, "HTTP rejected despite spoofed forwarded scheme");
        using var response = await client.GetAsync("/words");
        Check(response.IsSuccessStatusCode && response.Headers.GetValues("X-Content-Type-Options").Single() == "nosniff", "public content and security headers available");
        for (var i = 0; i < 10; i++)
            await client.PostAsJsonAsync("/users/login", new { UserId = "", Pw = "" });
        await Status(await client.PostAsJsonAsync("/users/login", new { UserId = "", Pw = "" }), HttpStatusCode.TooManyRequests, "credentials endpoint is rate limited");
    }

    private sealed record Credentials(int Id, string UserId, string AccessToken, DateTime ExpiresAt);

    private sealed class ApiFactory : WebApplicationFactory<global::Program>
    {
        public const string AdminToken = "test-only-admin-secret-not-for-production";
        private static readonly string Hash = BCrypt.Net.BCrypt.HashPassword(Password, 11);
        private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
        private bool _initialized;
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["Admin:Token"] = AdminToken, ["Security:TrustedProxy"] = "" }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                _connection.Open();
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
            });
        }
        public HttpClient Client()
        {
            var client = CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            if (!_initialized)
            {
                using var scope = Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.EnsureCreated();
                db.Users.AddRange(new User { Id = 1, UserId = "alice", Pw = Hash }, new User { Id = 2, UserId = "bob", Pw = Hash });
                db.Words.Add(new Word { Id = "item", English = "example" });
                db.Grammars.Add(new Grammar { Id = "item", Title = "example" });
                db.Idioms.Add(new Idiom { Id = "item", Title = "example" });
                db.SaveChanges();
                _initialized = true;
            }
            return client;
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _connection.Dispose();
        }
    }
}
