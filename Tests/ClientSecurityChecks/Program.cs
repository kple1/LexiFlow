using System.Net;
using System.Text.Json;
using LexiFlow.Models;
using LexiFlow.Services;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL " + message);
    checks++;
    Console.WriteLine("PASS " + message);
}
var alice = new SessionCredentials(1, "alice", new string('A', 64), DateTimeOffset.UtcNow.AddDays(7));
var bob = new SessionCredentials(2, "bob", new string('B', 64), DateTimeOffset.UtcNow.AddDays(7));
var session = new SessionService();
await SecureStorage.SetAsync("session_user_id", "alice");
Preferences.Set("sentence_archive_alice_v1", "legacy archive");
await session.RestoreAsync();
Check(!session.IsLoggedIn && session.CurrentUserId is null, "legacy id alone cannot sign in");
await session.SignInAsync(alice);
Check(Preferences.Get(LocalAccountData.Key(session, "archive"), "") == "legacy archive", "verified legacy owner retains archive");
Check(await SecureStorage.GetAsync("session_user_id") is null, "legacy identity marker removed after verified migration");
var restored = new SessionService();
await restored.RestoreAsync();
Check(restored.IsLoggedIn && restored.CurrentAccountId == 1 && restored.AccessToken == alice.AccessToken, "token session restores");

var handler = new FakeHandler();
using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/") };
var api = new ApiService(session, http);
await api.GetProgressAsync("alice");
Check(handler.Authorization == "Bearer " + alice.AccessToken, "private requests attach bearer token");
var sentBefore = handler.Sent;
try { await api.GetProgressAsync("bob"); throw new Exception("Cross-account request sent."); }
catch (InvalidOperationException) { Check(handler.Sent == sentBefore, "cross-account client request blocked before network"); }
await api.GetWordsAsync();
Check(handler.Authorization is null, "public requests do not leak bearer token");
using (var insecure = new HttpClient(handler) { BaseAddress = new Uri("http://example.invalid/") })
{
    try { _ = new ApiService(session, insecure); throw new Exception("HTTP accepted."); }
    catch (ArgumentException) { Check(true, "HTTP base URI rejected"); }
}
handler.Status = HttpStatusCode.Unauthorized;
try { await api.GetProgressAsync("alice"); } catch (HttpRequestException) { }
Check(!session.IsLoggedIn && await SecureStorage.GetAsync("session_credentials_v2") is null, "401 invalidates local and persisted session");
await session.SignInAsync(alice);
await session.SignInAsync(bob);
session.Invalidate(alice.AccessToken);
Check(session.IsLoggedIn && session.CurrentAccountId == 2, "late 401 cannot clear a newer account session");
handler.Status = HttpStatusCode.Forbidden;
try { await api.GetProgressAsync("bob"); } catch (HttpRequestException) { }
Check(session.IsLoggedIn, "authorization failure does not destroy valid authentication");

await session.SignInAsync(alice);
Preferences.Set(LocalAccountData.Key(session, "archive"), "alice only");
var aliceKey = LocalAccountData.Key(session, "archive");
await session.SignInAsync(new(3, "ALICE", new string('C', 64), DateTimeOffset.UtcNow.AddDays(7)));
Check(LocalAccountData.Key(session, "archive") != aliceKey && !Preferences.ContainsKey(LocalAccountData.Key(session, "archive")), "case variants have distinct local storage");
await session.SignInAsync(new(4, "alice", new string('D', 64), DateTimeOffset.UtcNow.AddDays(7)));
Check(!Preferences.ContainsKey(LocalAccountData.Key(session, "archive")), "re-registered name cannot inherit deleted account data");
await session.SignInAsync(bob);
Preferences.Set(LocalAccountData.Key(session, "archive"), "bob only");
LocalAccountData.DeleteCurrent(session);
Check(!Preferences.ContainsKey(LocalAccountData.Key(session, "archive")) && Preferences.Get(aliceKey, "") == "alice only", "local deletion is scoped to current numeric account");

await SecureStorage.SetAsync("session_credentials_v2", JsonSerializer.Serialize(alice with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }));
await restored.RestoreAsync();
Check(!restored.IsLoggedIn, "expired session does not restore");
await SecureStorage.SetAsync("session_credentials_v2", "{broken");
await restored.RestoreAsync();
Check(!restored.IsLoggedIn, "corrupt session fails closed");
try { await session.SignInAsync(alice with { AccessToken = "" }); throw new Exception("Invalid token accepted."); }
catch (InvalidOperationException) { Check(true, "legacy server login response without token rejected"); }
Console.WriteLine($"{checks} client security checks passed (no network, no device storage).");

sealed class FakeHandler : HttpMessageHandler
{
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public string? Authorization { get; private set; }
    public int Sent { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Sent++;
        Authorization = request.Headers.Authorization?.ToString();
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json") });
    }
}
