using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WordApp.Auth;
using WordApp.Data;
using WordApp.Models;
using WordApp.Services;

internal static partial class SecurityChecks
{
    private sealed class TestEmailSender : IAccountEmailSender
    {
        public bool IsConfigured => true;
        public bool Fail { get; set; }
        public List<(string To, string Subject, string Text)> Messages { get; } = [];
        public Task SendAsync(string to, string subject, string text, CancellationToken cancellation)
        {
            if (Fail) throw new InvalidOperationException("Test-only simulated SMTP failure");
            lock (Messages) Messages.Add((to, subject, text));
            return Task.CompletedTask;
        }
    }

    private static async Task Drain(ApiFactory factory)
    {
        var queue = factory.Services.GetRequiredService<AccountEmailQueue>();
        while (queue.TryRead(out var request))
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AccountEmailWorkflow>().ProcessAsync(request!);
        }
    }

    private static string LastToken(ApiFactory factory)
        => Regex.Match(factory.Mail.Messages.Last().Text, @"#[a-z-]+/([A-F0-9]{64})").Groups[1].Value;

    private static async Task SetEmail(ApiFactory factory, int id = 1, string email = "alice@example.test")
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.Id == id)
            .ExecuteUpdateAsync(update => update.SetProperty(u => u.VerifiedEmail, email).SetProperty(u => u.NormalizedEmail, email.ToUpperInvariant()));
    }

    private static async Task Issue(ApiFactory factory, string purpose, string email)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AccountEmailWorkflow>().ProcessAsync(new AccountMailRequest(purpose, email));
    }

    private static async Task RecoveryChecks()
    {
        foreach (var invalid in new[] { "Alice <alice@example.test>", "a@b\r\nBcc:x@y.test", ".a@example.test", "a..b@example.test", "한글@example.test", "a@-example.test" })
            Check(!RecoveryEmail.TryParse(invalid, out _, out _), "unsafe or unsupported email rejected");
        Check(RecoveryEmail.TryParse(" Alice+tag@Example.test ", out var original, out var normalized)
            && original == "Alice+tag@Example.test" && normalized == "ALICE+TAG@EXAMPLE.TEST", "email policy preserves delivery spelling and plus tag");
        await DisabledRecovery();
        await VerifiedSignup();
        await EmailBinding();
        await ResetFlow();
        await InvalidAndExpiredTokens();
        await EmailReplacement();
        await MailBudgetsAndFailure();
        var bounded = new AccountEmailQueue();
        Check(Enumerable.Range(0, 128).All(_ => bounded.Enqueue(new(AccountEmailWorkflow.Signup, "queue@example.test")))
            && !bounded.Enqueue(new(AccountEmailWorkflow.Signup, "queue@example.test")), "mail intent queue has a hard capacity");
        Check(!new AccountEmailOptions { PublicBaseUrl = "http://example.test/" }.ValidOrigin
            && !new AccountEmailOptions { PublicBaseUrl = "https://name:password@example.test/" }.ValidOrigin
            && !new AccountEmailOptions { PublicBaseUrl = "https://example.test/?next=attacker" }.ValidOrigin,
            "mail origin refuses HTTP, credentials and query strings");
    }

    private static async Task DisabledRecovery()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        await Status(await client.PostAsJsonAsync("/account/password/request", new { Email = "alice@example.test" }), HttpStatusCode.ServiceUnavailable, "unconfigured mail fails closed");
        await Login(client);
        await Status(await client.GetAsync("/users/me"), HttpStatusCode.OK, "legacy account still works without recovery email");
        Check(factory.Mail.Messages.Count == 0, "disabled email never sends a message");
    }

    private static async Task VerifiedSignup()
    {
        using var factory = new ApiFactory(true);
        using var client = factory.Client();
        await Status(await client.PostAsJsonAsync("/account/signup/request", new { Email = "new@example.test" }), HttpStatusCode.Accepted, "signup requests email proof");
        await Drain(factory);
        var token = LastToken(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Check(await db.Users.CountAsync() == 2, "no account or attacker-chosen password before email proof");
            var row = await db.AccountActionTokens.SingleAsync();
            Check(token.Length == 64 && row.TokenHash == SessionAuthenticationHandler.HashToken(token) && row.TokenHash != token, "email token stored only as a digest");
        }
        Check(factory.Mail.Messages.Single().Text.Contains("https://localhost/account/index.html#signup/"), "mail link uses configured HTTPS origin and a fragment");
        await Status(await client.PostAsJsonAsync("/account/signup/complete", new { Token = token, UserId = "new_user", Pw = "weak" }), HttpStatusCode.BadRequest, "weak signup password does not redeem proof");
        await Status(await client.PostAsJsonAsync("/account/signup/complete", new { Token = token, UserId = "new_user", Pw = Password }), HttpStatusCode.OK, "email proof creates verified account");
        await Status(await client.PostAsJsonAsync("/account/signup/complete", new { Token = token, UserId = "hijack", Pw = Password }), HttpStatusCode.BadRequest, "signup proof is single use");
        await Login(client, "new_user");
        using var final = factory.Services.CreateScope();
        var user = await final.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(u => u.UserId == "new_user");
        Check(user.VerifiedEmail == "new@example.test", "signup stores the email from proof, not request input");
    }

    private static async Task EmailBinding()
    {
        using var factory = new ApiFactory(true);
        using var client = factory.Client();
        await Login(client);
        await Status(await client.PostAsJsonAsync("/account/email/request", new { Email = "alice@example.test", CurrentPw = "wrong" }), HttpStatusCode.Forbidden, "email binding requires current password");
        await Status(await client.PostAsJsonAsync("/account/email/request", new { Email = "alice@example.test", CurrentPw = Password, UserId = 2 }), HttpStatusCode.Accepted, "email binding uses authenticated identity");
        await Drain(factory);
        var token = LastToken(factory);
        using (var scope = factory.Services.CreateScope())
            Check((await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.FindAsync(1))!.VerifiedEmail is null, "pending email is not usable for recovery");
        using var anonymous = factory.Client();
        await Status(await anonymous.PostAsJsonAsync("/account/email/confirm", new { Token = token }), HttpStatusCode.OK, "explicit confirmation links email");
        await Status(await client.GetAsync("/users/me"), HttpStatusCode.Unauthorized, "email change revokes old sessions");
        await Status(await anonymous.PostAsJsonAsync("/account/email/confirm", new { Token = token }), HttpStatusCode.BadRequest, "email confirmation cannot be replayed");
        using var checkScope = factory.Services.CreateScope();
        var users = await checkScope.ServiceProvider.GetRequiredService<AppDbContext>().Users.OrderBy(u => u.Id).ToListAsync();
        Check(users[0].VerifiedEmail == "alice@example.test" && users[1].VerifiedEmail is null, "other account not changed by submitted user id");
    }

    private static async Task ResetFlow()
    {
        using var factory = new ApiFactory(true);
        using var client = factory.Client();
        await SetEmail(factory);
        await Login(client);
        using var anonymous = factory.Client();
        using var known = await anonymous.PostAsJsonAsync("/account/password/request", new { Email = "ALICE@example.test" });
        using var unknown = await anonymous.PostAsJsonAsync("/account/password/request", new { Email = "nobody@example.test" });
        Check(known.StatusCode == HttpStatusCode.Accepted && known.StatusCode == unknown.StatusCode
            && await known.Content.ReadAsStringAsync() == await unknown.Content.ReadAsStringAsync(), "known and unknown reset requests are indistinguishable");
        Check(factory.Mail.Messages.Count == 0, "HTTP reset requests do not wait on SMTP or reveal delivery");
        await Drain(factory);
        Check(factory.Mail.Messages.Count == 1 && factory.Mail.Messages[0].To == "alice@example.test", "reset delivers only to persisted verified address");
        var token = LastToken(factory);
        await Status(await anonymous.PostAsJsonAsync("/account/password/reset", new { Token = token, Pw = "short" }), HttpStatusCode.BadRequest, "reset enforces password strength");
        await Status(await anonymous.PostAsJsonAsync("/account/password/reset", new { Token = token, Pw = "Replaced-password-2026" }), HttpStatusCode.OK, "verified reset succeeds");
        await Status(await anonymous.PostAsJsonAsync("/account/password/reset", new { Token = token, Pw = Password }), HttpStatusCode.BadRequest, "reset token is single use");
        await Status(await client.GetAsync("/users/me"), HttpStatusCode.Unauthorized, "reset revokes active device session");
        await Status(await client.PostAsJsonAsync("/users/login", new { UserId = "alice", Pw = Password }), HttpStatusCode.Unauthorized, "old password rejected after reset");
        await Login(client, password: "Replaced-password-2026");
        await Drain(factory);
        Check(factory.Mail.Messages.Count == 2 && !factory.Mail.Messages[1].Text.Contains(token), "reset sends a token-free change notice despite request cooldown");
    }

    private static async Task InvalidAndExpiredTokens()
    {
        using var factory = new ApiFactory(true);
        using var client = factory.Client();
        await SetEmail(factory);
        await Issue(factory, AccountEmailWorkflow.Reset, "alice@example.test");
        var reset = LastToken(factory);
        await Status(await client.PostAsJsonAsync("/account/email/confirm", new { Token = reset }), HttpStatusCode.BadRequest, "reset bearer cannot verify an email");
        await Status(await client.PostAsJsonAsync("/account/password/reset", new { Token = new string('F', 64), Pw = Password }), HttpStatusCode.BadRequest, "forged token denied");
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().AccountActionTokens.ExecuteUpdateAsync(t => t.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
        await Status(await client.PostAsJsonAsync("/account/password/reset", new { Token = reset, Pw = Password }), HttpStatusCode.BadRequest, "expired token denied");
        await Login(client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.AccountActionTokens.ExecuteUpdateAsync(t => t.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(1)));
        }
        await Status(await client.PatchAsJsonAsync("/users/1", new { CurrentPw = Password, Pw = "Changed-on-device-2026" }), HttpStatusCode.NoContent, "regular password change succeeds");
        await Status(await client.PostAsJsonAsync("/account/password/reset", new { Token = reset, Pw = Password }), HttpStatusCode.BadRequest, "regular password change invalidates pending reset");
    }

    private static async Task MailBudgetsAndFailure()
    {
        using var factory = new ApiFactory(true);
        using var client = factory.Client();
        await Issue(factory, AccountEmailWorkflow.Signup, "rate@example.test");
        await Issue(factory, AccountEmailWorkflow.Signup, "RATE@example.test");
        Check(factory.Mail.Messages.Count == 1, "persistent recipient budget prevents duplicate mail across workflow instances");
        using (var budgetScope = factory.Services.CreateScope())
            Check(await budgetScope.ServiceProvider.GetRequiredService<AppDbContext>().AccountMailBudgets
                .Where(b => b.Key.StartsWith("global-")).AllAsync(b => b.Count == 1), "blocked mail rolls back every global budget increment");
        factory.Mail.Fail = true;
        var failed = false;
        try { await Issue(factory, AccountEmailWorkflow.Signup, "smtp-failure@example.test"); }
        catch (InvalidOperationException) { failed = true; }
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Check(failed && !await db.AccountActionTokens.AnyAsync(t => t.Email == "smtp-failure@example.test"), "SMTP failure revokes undelivered token");
        Check(!await db.AccountMailBudgets.AnyAsync(b => b.Key.Contains("@")), "persistent rate keys do not expose raw addresses");
        for (var i = 0; i < 6; i++) await client.PostAsJsonAsync("/account/password/request", new { Email = "nobody@example.test" });
        await Status(await client.PostAsJsonAsync("/account/password/request", new { Email = "nobody@example.test" }), HttpStatusCode.TooManyRequests, "account email endpoint has an IP rate limit");
    }

    private static async Task EmailReplacement()
    {
        using var factory = new ApiFactory(true);
        using var client = factory.Client();
        await SetEmail(factory);
        await SetEmail(factory, 2, "bob@example.test");
        await Login(client);
        await Status(await client.PostAsJsonAsync("/account/email/request", new { Email = "bob@example.test", CurrentPw = Password }), HttpStatusCode.Accepted, "another verified email gets generic binding response");
        await Drain(factory);
        Check(factory.Mail.Messages.Count == 0, "cannot claim or mail an address already bound to another account");
        await Issue(factory, AccountEmailWorkflow.Reset, "alice@example.test");
        var oldReset = LastToken(factory);
        await Status(await client.PostAsJsonAsync("/account/email/request", new { Email = "new-alice@example.test", CurrentPw = Password }), HttpStatusCode.Accepted, "request replacement recovery email");
        await Drain(factory);
        var verifyNew = LastToken(factory);
        using (var scope = factory.Services.CreateScope())
            Check((await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.FindAsync(1))!.VerifiedEmail == "alice@example.test", "old verified email retained until replacement is confirmed");
        using var anonymous = factory.Client();
        await Status(await anonymous.PostAsJsonAsync("/account/email/confirm", new { Token = verifyNew }), HttpStatusCode.OK, "replacement email verified");
        await Status(await anonymous.PostAsJsonAsync("/account/password/reset", new { Token = oldReset, Pw = Password }), HttpStatusCode.BadRequest, "old address reset proof invalidated by email replacement");
        await Drain(factory);
        Check(factory.Mail.Messages.Last().To == "alice@example.test" && !factory.Mail.Messages.Last().Text.Contains('#'), "old verified address receives token-free email change notice");
        await Login(client);
        await Status(await client.GetAsync("/account/email"), HttpStatusCode.OK, "authenticated owner can read recovery email");
    }
}
