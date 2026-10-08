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
handler.Status = HttpStatusCode.ServiceUnavailable;
var offlineWords = await api.GetWordsAsync();
Check(offlineWords.Count == 360 && api.LastWordLoadUsedFallback, "public content failure provides the full bundled vocabulary with explicit fallback state");
Check(handler.Authorization is null && session.IsLoggedIn, "content fallback neither leaks credentials nor clears the account session");
handler.Status = HttpStatusCode.OK;
Check((await api.GetWordsAsync()).Count == 360 && !api.LastWordLoadUsedFallback, "successful content refresh clears fallback state and merges the bundled pack");
var builtInId = offlineWords[0].Id;
var sentBeforeLocal = handler.Sent;
await api.UpsertProgressAsync("alice", builtInId, false);
await api.UpsertProgressAsync("alice", builtInId, true, "Mastered");
var localProgress = api.GetLocalWordProgress("alice").Single();
Check(handler.Sent == sentBeforeLocal && localProgress.CorrectCount == 1 && localProgress.WrongCount == 1 && localProgress.Status == "Mastered",
    "bundled progress persists correct/wrong counts locally without a network request");
Check(localProgress.LastReviewed is not null && localProgress.UpdatedAt.Kind == DateTimeKind.Utc,
    "bundled review timestamps use UTC");
handler.Body = "[{\"wordId\":\"server-word\",\"status\":\"Learning\"}]";
var mergedProgress = await api.GetProgressAsync("alice");
Check(mergedProgress.Count == 2 && mergedProgress.Any(item => item.WordId == builtInId) && mergedProgress.Any(item => item.WordId == "server-word"),
    "server progress and bundled device progress are both returned");
var beforeServerSave = handler.Sent;
await api.UpsertProgressAsync("alice", "server-word", true);
Check(handler.Sent == beforeServerSave + 1 && handler.Path == "/users/alice/progress" && handler.Authorization == "Bearer " + alice.AccessToken,
    "existing server words still save through the authenticated API");
try { await api.UpsertProgressAsync("bob", builtInId, true); throw new Exception("Cross-account bundled write accepted."); }
catch (InvalidOperationException) { Check(true, "bundled progress cannot be written for another account"); }
try { await api.UpsertProgressAsync("alice", "lexicore-v1:missing", true); throw new Exception("Unknown bundled ID accepted."); }
catch (ArgumentException) { Check(true, "unknown bundled identities cannot be persisted"); }
try { await api.UpsertProgressAsync("alice", builtInId, true, "anything"); throw new Exception("Invalid status accepted."); }
catch (ArgumentException) { Check(true, "bundled progress rejects invalid status labels"); }
await session.SignInAsync(bob);
Check(api.GetLocalWordProgress("bob").Count == 0, "another account cannot inherit bundled progress");
await api.UpsertProgressAsync("bob", builtInId, false);
LocalAccountData.DeleteCurrent(session);
Check(api.GetLocalWordProgress("bob").Count == 0, "account deletion removes its bundled progress");
await session.SignInAsync(alice);
Check(api.GetLocalWordProgress("alice").Single().CorrectCount == 1, "deleting another account preserves the original bundled progress");
Preferences.FailWrites = true;
try { await api.UpsertProgressAsync("alice", builtInId, true); throw new Exception("Failed write reported success."); }
catch (IOException) { Check(true, "device write failure propagates instead of reporting a saved review"); }
finally { Preferences.FailWrites = false; }
Check(api.GetLocalWordProgress("alice").Single().CorrectCount == 1, "failed persistence cannot mutate the stored progress");
var localKey = LocalAccountData.Key(session, BuiltInWordProgressService.StorageSuffixFor(builtInId));
var savedLocal = Preferences.Get(localKey, "[]");
Preferences.Set(localKey, "{broken");
try { _ = api.GetLocalWordProgress("alice"); throw new Exception("Corrupt records hidden."); }
catch (JsonException) { Check(true, "corrupt bundled records surface an error instead of being silently reset"); }
finally { Preferences.Set(localKey, savedLocal); }
handler.Body = "[]";
handler.Body = "{\"email\":\"alice@example.test\",\"enabled\":true}";
var recoveryStatus = await api.GetRecoveryEmailAsync();
Check(recoveryStatus?.Email == "alice@example.test" && recoveryStatus.Enabled
    && handler.Authorization == "Bearer " + alice.AccessToken, "recovery email status requires bearer authentication");
handler.Body = "{}";
await api.RequestRecoveryEmailAsync("alice@example.test", "Current-password-2026");
Check(handler.Path == "/account/email/request" && handler.Authorization == "Bearer " + alice.AccessToken
    && handler.RequestBody!.Contains("Current-password-2026"), "email binding sends current password only in authenticated HTTPS body");
handler.Body = "[]";
using (var insecure = new HttpClient(handler) { BaseAddress = new Uri("http://example.invalid/") })
{
    try { _ = new ApiService(session, insecure); throw new Exception("HTTP accepted."); }
    catch (ArgumentException) { Check(true, "HTTP base URI rejected"); }
}
handler.Body = JsonSerializer.Serialize(new RankingSnapshot { GeneratedAt = DateTime.UtcNow, ParticipantCount = 1,
    Entries = [new RankingEntry { Rank = 1, Nickname = "Reader", MasteredWords = 2, Score = 2, IsMe = true }],
    Me = new RankingMe { Participating = true, Nickname = "Reader", Rank = 1, MasteredWords = 2, Score = 2 } });
var validRankingBody = handler.Body;
Check(new RankingEntry { Rank = 1 }.IsChampion && !new RankingEntry { Rank = 2 }.IsChampion,
    "chroma applies only to first place, including tied champions");
var longLoginId = "reader.name.with.a.long.login.id";
handler.Body = validRankingBody.Replace("Reader", longLoginId);
Check((await api.GetRankingAsync()).Me.Nickname == longLoginId, "long dotted login IDs render without nickname restrictions");
handler.Body = validRankingBody.Replace("Reader", new string('X', 100));
Check((await api.GetRankingAsync()).Me.Nickname.Length == 100, "legacy IDs up to the database limit are supported");
handler.Body = validRankingBody.Replace("Reader", new string('X', 101));
try { await api.GetRankingAsync(); throw new Exception("Oversized ID accepted."); }
catch (JsonException) { Check(true, "oversized ranking identity fails closed"); }
handler.Body = validRankingBody;
var ranking = await api.GetRankingAsync();
Check(ranking.Me.Score == 2 && handler.Path == "/ranking" && handler.Authorization == "Bearer " + alice.AccessToken,
    "ranking requires authentication and validates server score");
Check(handler.RequestBody is null && handler.Path == "/ranking", "automatic ranking needs no nickname, opt-in or client score submission");
handler.Body = validRankingBody.Replace("\"Participating\":true", "\"Participating\":false");
try { await api.GetRankingAsync(); throw new Exception("Manual participation accepted."); }
catch (JsonException) { Check(true, "automatic ranking requires own participation"); }
handler.Body = "{}";
try { await api.GetRankingAsync(); throw new Exception("Incomplete ranking accepted."); }
catch (JsonException) { Check(true, "missing ranking protocol fields fail closed"); }
handler.Body = validRankingBody.Replace("\"Score\":2", "\"Score\":99");
try { await api.GetRankingAsync(); throw new Exception("Invented score accepted."); }
catch (JsonException) { Check(true, "ranking score must equal its server category counts"); }
handler.Status = HttpStatusCode.NotFound;
try { await api.GetRankingAsync(); throw new Exception("Missing ranking hidden."); }
catch (HttpRequestException error) { Check(error.StatusCode == HttpStatusCode.NotFound, "undeployed ranking is explicit, never a fake leaderboard"); }
handler.Status = HttpStatusCode.OK;
handler.Body = validRankingBody;
handler.BeforeReturn = () => session.SignInAsync(bob);
try { await api.GetRankingAsync(); throw new Exception("Previous account response leaked."); }
catch (InvalidOperationException) { Check(true, "delayed ranking response cannot appear under a different account"); }
handler.BeforeReturn = null;
await session.SignInAsync(alice);
handler.Body = "[]";
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
    public string Body { get; set; } = "[]";
    public string? Path { get; private set; }
    public string? RequestBody { get; private set; }
    public Func<Task>? BeforeReturn { get; set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Sent++;
        Authorization = request.Headers.Authorization?.ToString();
        Path = request.RequestUri?.AbsolutePath;
        RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        if (BeforeReturn is not null) await BeforeReturn();
        return new HttpResponseMessage(Status) { Content = new StringContent(Body, System.Text.Encoding.UTF8, "application/json") };
    }
}
