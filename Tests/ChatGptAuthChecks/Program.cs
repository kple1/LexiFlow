using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LexiFlow.Services;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

var checks = new List<(string Name, Func<Task> Run)>();
void Check(string name, Func<Task> run) => checks.Add((name, run));
void Assert(bool value) { if (!value) throw new Exception("Assertion failed (details redacted)."); }
async Task Throws(ChatGptFailureKind kind, Func<Task> action)
{
    try { await action(); }
    catch (ChatGptException ex) when (ex.Kind == kind) { return; }
    throw new Exception("Expected safe error was not returned.");
}

Check("PKCE and authorization isolate registration from reauthorization", () =>
{
    var verifier = ChatGptOAuthClient.RandomValue();
    Assert(verifier.Length == 43 && verifier != ChatGptOAuthClient.RandomValue());
    Assert(ChatGptOAuthClient.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk") == "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
    var callback = new Uri("http://127.0.0.1:45555/auth/callback");
    var first = Query(ChatGptOAuthClient.AuthorizationUri(null, "urn:uuid:test", callback, "state", "nonce", verifier));
    Assert(first["client_id"] == "dynamic_agent_client" && first["agent_name_hint"] == "LexiFlow");
    Assert(first["resource"] == "https://api.openai.com/v1" && first["redirect_uri"] == callback.AbsoluteUri);
    Assert(first["scope"].Contains("chatgpt.tokens.use.direct") && !first.ContainsKey("id_token_hint"));
    var returning = Query(ChatGptOAuthClient.AuthorizationUri("oaiapp_saved", "urn:uuid:test", callback, "state", "nonce", verifier));
    Assert(returning["client_id"] == "oaiapp_saved" && !returning.ContainsKey("agent_name_hint"));
    return Task.CompletedTask;
});

Check("Callback validates state, exact path, issued client and duplicate parameters", async () =>
{
    var valid = ChatGptLoopbackCallback.ParseCallback("/auth/callback?code=fake&state=good&client_id=oaiapp_test", "good", null);
    Assert(valid.ClientId == "oaiapp_test");
    Assert(ChatGptLoopbackCallback.ParseCallback("/auth/callback?code=fake&state=good", "good", "oaiapp_saved").ClientId == "oaiapp_saved");
    foreach (var target in new[]
    {
        "/callback?code=fake&state=good&client_id=oaiapp_test", "/auth/callback?code=fake&state=bad&client_id=oaiapp_test",
        "/auth/callback?code=fake&state=good&state=good&client_id=oaiapp_test", "/auth/callback?code=fake&state=good",
        "/auth/callback?code=fake&state=good&client_id=dynamic_agent_client"
    }) await Throws(ChatGptFailureKind.InvalidResponse, () => Task.FromResult(ChatGptLoopbackCallback.ParseCallback(target, "good", null)));
    await Throws(ChatGptFailureKind.InvalidResponse, () => Task.FromResult(ChatGptLoopbackCallback.ParseCallback("/auth/callback?code=fake&state=good&client_id=oaiapp_other", "good", "oaiapp_saved")));
    await Throws(ChatGptFailureKind.SignInCancelled, () => Task.FromResult(ChatGptLoopbackCallback.ParseCallback("/auth/callback?error=access_denied&state=good", "good", null)));
});

Check("Protected storage chunks and failed staged write preserve the last committed value", async () =>
{
    var storage = new FakeStore();
    var store = new ChatGptProtectedStore(storage);
    await store.WriteAsync("test", new TestPayload { Text = new string('a', 17000) });
    Assert(storage.Values.Values.All(s => s.Length <= 1500));
    Assert((await store.ReadAsync<TestPayload>("test"))?.Text.Length == 17000);
    var calls = 0;
    storage.FailWrite = (_, _) => ++calls == 2;
    await Throws(ChatGptFailureKind.StorageUnavailable, () => store.WriteAsync("test", new TestPayload { Text = new string('b', 19000) }));
    storage.FailWrite = null;
    Assert((await store.ReadAsync<TestPayload>("test"))?.Text == new string('a', 17000));
    await store.WriteAsync("test", new TestPayload { Text = "replacement" });
    Assert(!storage.Values.Values.Any(s => s.Contains(new string('a', 100), StringComparison.Ordinal)));
});

Check("Corrupt secure-store chunks fail closed", async () =>
{
    var storage = new FakeStore();
    var store = new ChatGptProtectedStore(storage);
    await store.WriteAsync("test", new TestPayload { Text = "original" });
    storage.Values[storage.Values.Keys.Single(k => k.EndsWith("_0", StringComparison.Ordinal))] = "corrupt";
    await Throws(ChatGptFailureKind.StorageUnavailable, async () => { await store.ReadAsync<TestPayload>("test"); });
});

Check("Signed ID tokens enforce signature, issuer, audience, nonce, subject and lifetime", async () =>
{
    using var issuer = new FakeIssuer();
    using var http = issuer.Client();
    var oauth = new ChatGptOAuthClient(http);
    var identity = await oauth.ValidateIdentityAsync(issuer.Identity("nonce"), "oaiapp_test", "nonce", null, default);
    Assert(identity.Subject == "subject-test");
    await Throws(ChatGptFailureKind.InvalidResponse, () => oauth.ValidateIdentityAsync(issuer.Identity("other"), "oaiapp_test", "nonce", null, default));
    await Throws(ChatGptFailureKind.InvalidResponse, () => oauth.ValidateIdentityAsync(issuer.Identity("nonce", audience: "oaiapp_wrong"), "oaiapp_test", "nonce", null, default));
    await Throws(ChatGptFailureKind.InvalidResponse, () => oauth.ValidateIdentityAsync(issuer.Identity("nonce", tokenIssuer: "https://not-openai.invalid"), "oaiapp_test", "nonce", null, default));
    await Throws(ChatGptFailureKind.InvalidResponse, () => oauth.ValidateIdentityAsync(issuer.Identity("nonce"), "oaiapp_test", "nonce", "other-subject", default));
    await Throws(ChatGptFailureKind.InvalidResponse, () => oauth.ValidateIdentityAsync(issuer.Identity("nonce", expired: true), "oaiapp_test", "nonce", null, default));
    await Throws(ChatGptFailureKind.InvalidResponse, () => oauth.ValidateIdentityAsync(issuer.Identity("nonce", azp: "oaiapp_wrong"), "oaiapp_test", "nonce", null, default));
    await Throws(ChatGptFailureKind.InvalidResponse, () => oauth.ValidateIdentityAsync(issuer.Identity("nonce", multipleAudiences: true), "oaiapp_test", "nonce", null, default));
    await oauth.ValidateIdentityAsync(issuer.Identity("nonce", azp: "oaiapp_test", multipleAudiences: true), "oaiapp_test", "nonce", null, default);
    using var wrongKey = new FakeIssuer();
    await Throws(ChatGptFailureKind.InvalidResponse, () => oauth.ValidateIdentityAsync(wrongKey.Identity("nonce"), "oaiapp_test", "nonce", null, default));
});

Check("Identity-only grants cannot enable inference", async () =>
{
    using var fixture = new Fixture();
    await fixture.SeedAsync(scope: "openid profile email");
    await fixture.Service.RestoreAsync();
    Assert(fixture.Service.IsConnected && !fixture.Service.PlanUsageEnabled);
    await Throws(ChatGptFailureKind.PlanNotEnabled, () => fixture.Service.GetGrantAsync());
    Assert(fixture.Issuer.TokenRequests == 0);
});

Check("LexiFlow owner change and eventless expiration invalidate a grant", async () =>
{
    using var fixture = new Fixture();
    await fixture.SeedAsync();
    await fixture.Service.RestoreAsync();
    var grant = await fixture.Service.GetGrantAsync();
    Assert(!grant.ToString().Contains(grant.AccessToken, StringComparison.Ordinal));
    fixture.Session.IsLoggedIn = false;
    Assert(!fixture.Service.IsConnected && fixture.Service.ConnectionVersion != grant.Version);
    await Throws(ChatGptFailureKind.SessionChanged, () => fixture.Service.GetGrantAsync());
    fixture.Session.Switch(22);
    await fixture.Service.RestoreAsync();
    await Throws(ChatGptFailureKind.NotConnected, () => fixture.Service.GetGrantAsync());
    Assert(fixture.Issuer.TokenRequests == 0);
});

Check("Refresh preserves connection version and replaces credentials atomically", async () =>
{
    using var fixture = new Fixture();
    await fixture.SeedAsync(expiring: true);
    await fixture.Service.RestoreAsync();
    var version = fixture.Service.ConnectionVersion;
    var first = await fixture.Service.GetGrantAsync();
    var second = await fixture.Service.GetGrantAsync();
    Assert(first.AccessToken == "fake-access-new" && second.Version == version && fixture.Issuer.TokenRequests == 1);
    var form = fixture.Issuer.LastForm!;
    Assert(form["grant_type"] == "refresh_token" && form["client_id"] == "oaiapp_test" && !form.ContainsKey("scope") && !form.ContainsKey("client_secret"));
    Assert((await fixture.ReadAsync()).Profiles[0].Credentials?.RefreshToken == "fake-refresh-new");
});

Check("Two service instances reread rotated credentials under the process lock", async () =>
{
    using var fixture = new Fixture();
    await fixture.SeedAsync(expiring: true);
    var second = new ChatGptConnectionService(fixture.Session, fixture.Storage, fixture.Http, () => true);
    await Task.WhenAll(fixture.Service.RestoreAsync(), second.RestoreAsync());
    await Task.WhenAll(fixture.Service.GetGrantAsync(), second.GetGrantAsync());
    Assert(fixture.Issuer.TokenRequests == 1);
});

Check("Transient refresh failure retains credentials; invalid_grant clears tokens only", async () =>
{
    using var fixture = new Fixture();
    await fixture.SeedAsync(expiring: true);
    fixture.Issuer.TokenError = "temporarily_unavailable";
    await Throws(ChatGptFailureKind.Unavailable, () => fixture.Service.GetGrantAsync());
    Assert((await fixture.ReadAsync()).Profiles[0].Credentials is not null);
    fixture.Issuer.TokenError = "invalid_grant";
    await Throws(ChatGptFailureKind.NotConnected, () => fixture.Service.GetGrantAsync());
    var profile = (await fixture.ReadAsync()).Profiles[0];
    Assert(profile.Credentials is null && profile.ClientId == "oaiapp_test" && profile.Subject == "subject-test");
});

Check("Owner change during refresh never publishes the old owner's credentials", async () =>
{
    using var fixture = new Fixture();
    await fixture.SeedAsync(expiring: true);
    fixture.Issuer.BeforeToken = () => fixture.Session.Switch(22);
    await Throws(ChatGptFailureKind.SessionChanged, () => fixture.Service.GetGrantAsync());
    Assert(!fixture.Service.IsConnected);
});

Check("Disconnect attempts remote revocation and preserves registration mapping", async () =>
{
    using var fixture = new Fixture();
    await fixture.SeedAsync();
    await fixture.Service.RestoreAsync();
    Assert(await fixture.Service.DisconnectAsync());
    var record = await fixture.ReadAsync();
    Assert(record.Profiles[0].Credentials is null && record.Profiles[0].Subject == "subject-test" && fixture.Issuer.Revocations == 1);
    Assert(await fixture.Service.DisconnectAsync());
    await fixture.Service.RestoreAsync();
    Assert(fixture.Service.Profiles.Count == 1);
    await Throws(ChatGptFailureKind.NotConnected, () => fixture.Service.GetGrantAsync());
});

Check("Failed revocation is reported but local tokens are cleared", async () =>
{
    using var fixture = new Fixture();
    await fixture.SeedAsync();
    fixture.Issuer.RevocationFails = true;
    Assert(!await fixture.Service.DisconnectAsync());
    Assert((await fixture.ReadAsync()).Profiles[0].Credentials is null);
});

Check("Account deletion clears every local registration and blocks a failed cleanup", async () =>
{
    using var fixture = new Fixture();
    await fixture.SeedAsync();
    fixture.Storage.FailWrite = (_, _) => true;
    await Throws(ChatGptFailureKind.StorageUnavailable, () => fixture.Service.ForgetCurrentOwnerAsync());
    Assert(!fixture.Service.IsConnected);
    await Throws(ChatGptFailureKind.NotConnected, () => fixture.Service.GetGrantAsync());
    fixture.Storage.FailWrite = null;
    await fixture.Service.ForgetCurrentOwnerAsync();
    Assert((await fixture.ReadAsync()).Profiles.Count == 0);
});

Check("New registration uses only loopback and stores issued client before exchange failure", async () =>
{
    using var fixture = new Fixture();
    fixture.Issuer.TokenError = "invalid_grant";
    Uri? authorization = null;
    await Throws(ChatGptFailureKind.NotConnected, () => fixture.Service.ConnectAsync(async uri =>
    {
        authorization = uri;
        var query = Query(uri);
        await SendLocalCallbackAsync(query["redirect_uri"], query["state"], "oaiapp_test");
    }));
    var saved = await fixture.ReadAsync();
    Assert(saved.Profiles.Count == 1 && saved.Profiles[0].ClientId == "oaiapp_test" && saved.Profiles[0].Credentials is null);
    Assert(Query(authorization!)["client_id"] == "dynamic_agent_client");
    fixture.Issuer.TokenError = null;
    await fixture.Service.ConnectAsync(async uri =>
    {
        var query = Query(uri);
        Assert(query["client_id"] == "oaiapp_test" && !query.ContainsKey("agent_name_hint"));
        fixture.Issuer.Nonce = query["nonce"];
        await SendLocalCallbackAsync(query["redirect_uri"], query["state"], null);
    });
    Assert(fixture.Service.IsConnected && fixture.Service.PlanUsageEnabled && (await fixture.ReadAsync()).Profiles.Count == 1);
    Assert(fixture.Issuer.LastForm!["client_id"] == "oaiapp_test" && fixture.Issuer.LastForm.ContainsKey("code_verifier"));
});

Check("Non-Windows guard prevents browser and HTTP work", async () =>
{
    using var fixture = new Fixture();
    var unsupported = new ChatGptConnectionService(fixture.Session, fixture.Storage, fixture.Http, () => false);
    await Throws(ChatGptFailureKind.Unsupported, () => unsupported.ConnectAsync(_ => throw new Exception("Browser must not open.")));
    Assert(fixture.Issuer.TokenRequests == 0);
});

Check("Cancellation during credential commit cannot activate the account after restore", async () =>
{
    using var fixture = new Fixture();
    using var cancellation = new CancellationTokenSource();
    fixture.Storage.FailWrite = (_, value) =>
    {
        if (value.Contains("fake-access-new", StringComparison.Ordinal)) cancellation.Cancel();
        return false;
    };
    try
    {
        await fixture.Service.ConnectAsync(async uri =>
        {
            var query = Query(uri);
            fixture.Issuer.Nonce = query["nonce"];
            await SendLocalCallbackAsync(query["redirect_uri"], query["state"], "oaiapp_test");
        }, cancellationToken: cancellation.Token);
        throw new Exception("Expected cancellation.");
    }
    catch (OperationCanceledException) { }
    fixture.Storage.FailWrite = null;
    await fixture.Service.RestoreAsync();
    Assert(!fixture.Service.IsConnected && (await fixture.ReadAsync()).Profiles.All(p => p.Credentials is null));
});

foreach (var check in checks)
{
    try { await check.Run(); Console.WriteLine("PASS " + check.Name); }
    catch { Console.Error.WriteLine("FAIL " + check.Name + " (exception details intentionally redacted)"); return 1; }
}
Console.WriteLine($"Passed {checks.Count} auth checks. Only in-memory credentials, fake HTTP and IPv4 loopback were used.");
return 0;

static Dictionary<string, string> Query(Uri uri) => uri.Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2))
    .ToDictionary(x => Uri.UnescapeDataString(x[0]), x => Uri.UnescapeDataString(x[1].Replace('+', ' ')));

static async Task SendLocalCallbackAsync(string redirect, string state, string? clientId)
{
    var uri = new Uri(redirect);
    if (uri.Host != "127.0.0.1" || uri.Scheme != "http") throw new Exception("Non-loopback callback rejected by test.");
    using var client = new TcpClient(AddressFamily.InterNetwork);
    await client.ConnectAsync(IPAddress.Loopback, uri.Port);
    var path = uri.AbsolutePath + "?code=fake-code&state=" + Uri.EscapeDataString(state)
        + (clientId is null ? "" : "&client_id=" + Uri.EscapeDataString(clientId));
    await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: {uri.Authority}\r\nConnection: close\r\n\r\n"));
}

sealed class TestPayload { public string Text { get; set; } = ""; }

sealed class FakeStore : IChatGptSecureStore
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
    public Func<string, string, bool>? FailWrite { get; set; }
    public Task<string?> GetAsync(string key) => Task.FromResult(Values.GetValueOrDefault(key));
    public Task SetAsync(string key, string value)
    {
        if (FailWrite?.Invoke(key, value) == true) throw new IOException("Injected storage failure.");
        Values[key] = value;
        return Task.CompletedTask;
    }
    public void Remove(string key) => Values.Remove(key);
}

sealed class Fixture : IDisposable
{
    public FakeStore Storage { get; } = new();
    public FakeIssuer Issuer { get; } = new();
    public SessionService Session { get; } = new();
    public HttpClient Http { get; }
    public ChatGptConnectionService Service { get; }
    public Fixture() { Http = Issuer.Client(); Service = new ChatGptConnectionService(Session, Storage, Http, () => true); }
    public Task SeedAsync(bool expiring = false, string scope = ChatGptOAuthClient.Scopes)
    {
        var profile = new ChatGptProfileRecord
        {
            ClientId = "oaiapp_test", Subject = "subject-test", Email = "test@example.invalid",
            Credentials = new ChatGptCredentials
            {
                AccessToken = "fake-access-old", RefreshToken = "fake-refresh-old", IdToken = Issuer.Identity("old-nonce"), Scope = scope,
                SavedAt = DateTimeOffset.UtcNow.AddMinutes(-30), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(expiring ? 0.5 : 30)
            }
        };
        return new ChatGptProtectedStore(Storage).WriteAsync("owner:11", new ChatGptOwnerRecord { OwnerKey = "11", ActiveProfileId = profile.Id, Profiles = [profile] });
    }
    public async Task<ChatGptOwnerRecord> ReadAsync() => (await new ChatGptProtectedStore(Storage).ReadAsync<ChatGptOwnerRecord>("owner:11"))!;
    public void Dispose() { Http.Dispose(); Issuer.Dispose(); }
}

sealed class FakeIssuer : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    public string Nonce { get; set; } = "refresh-nonce";
    public int TokenRequests { get; private set; }
    public int Revocations { get; private set; }
    public string? TokenError { get; set; }
    public bool RevocationFails { get; set; }
    public Action? BeforeToken { get; set; }
    public Dictionary<string, string>? LastForm { get; private set; }
    public string Identity(string nonce, string audience = "oaiapp_test", string tokenIssuer = ChatGptOAuthClient.Issuer,
        bool expired = false, string? azp = null, bool multipleAudiences = false)
    {
        var claims = new Dictionary<string, object> { ["sub"] = "subject-test", ["email"] = "test@example.invalid", ["nonce"] = nonce };
        if (azp is not null) claims["azp"] = azp;
        if (multipleAudiences) claims["aud"] = new[] { audience, "another-audience" };
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = tokenIssuer, Audience = multipleAudiences ? null : audience,
            IssuedAt = DateTime.UtcNow.AddMinutes(-5), NotBefore = DateTime.UtcNow.AddMinutes(-5),
            Expires = expired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddHours(1),
            Claims = claims,
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = "test-signing-key" }, SecurityAlgorithms.RsaSha256)
        });
    }
    public HttpClient Client() => new(new FakeHandler(HandleAsync));
    private async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri?.Host != "auth.openai.com") throw new InvalidOperationException("Unexpected fake transport destination.");
        var path = request.RequestUri.AbsolutePath;
        if (path == "/.well-known/jwks.json")
        {
            var key = _rsa.ExportParameters(false);
            return Json(new { keys = new[] { new { kty = "RSA", use = "sig", alg = "RS256", kid = "test-signing-key", n = Base64UrlEncoder.Encode(key.Modulus), e = Base64UrlEncoder.Encode(key.Exponent) } } });
        }
        if (path == "/.well-known/openid-configuration") return Json(new { issuer = ChatGptOAuthClient.Issuer, revocation_endpoint = ChatGptOAuthClient.Issuer + "/fake-revoke" });
        if (path == "/fake-revoke") { Revocations++; return new HttpResponseMessage(RevocationFails ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = new StringContent("") }; }
        if (path != "/api/accounts/oauth/token") throw new InvalidOperationException("Unexpected fake request.");
        TokenRequests++;
        LastForm = (await request.Content!.ReadAsStringAsync(ct)).Split('&').Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
        BeforeToken?.Invoke();
        if (TokenError is not null) return Json(new { error = TokenError }, TokenError == "invalid_grant" ? HttpStatusCode.BadRequest : HttpStatusCode.ServiceUnavailable);
        return Json(new { access_token = "fake-access-new", refresh_token = "fake-refresh-new", id_token = Identity(Nonce), scope = ChatGptOAuthClient.Scopes, token_type = "Bearer", expires_in = 3600 });
    }
    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    public void Dispose() => _rsa.Dispose();
}

sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request, cancellationToken);
}
