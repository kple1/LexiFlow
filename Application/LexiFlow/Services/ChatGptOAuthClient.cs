using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LexiFlow.Services;

internal sealed class ChatGptOAuthClient(HttpClient http)
{
    internal const string Issuer = "https://auth.openai.com";
    internal const string Resource = "https://api.openai.com/v1";
    internal const string DirectScope = "chatgpt.tokens.use.direct";
    internal const string Scopes = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct";
    internal const string TokenEndpoint = Issuer + "/api/accounts/oauth/token";
    private const int ResponseLimit = 128 * 1024;
    private IReadOnlyCollection<SecurityKey>? _keys;
    private DateTimeOffset _keysReadAt;

    internal static HttpClient CreateHttpClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    }) { Timeout = Timeout.InfiniteTimeSpan };

    internal static bool IsIssuedClientId(string? clientId) => clientId is { Length: > 7 and <= 200 }
        && clientId.StartsWith("oaiapp_", StringComparison.Ordinal)
        && clientId.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    internal static string RandomValue() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    internal static string Challenge(string verifier) => Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    internal static bool FixedEquals(string? left, string? right) => left is not null && right is not null
        && left.Length == right.Length
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    internal static Uri AuthorizationUri(string? clientId, string hostId, Uri callback, string state, string nonce, string verifier)
    {
        var values = new Dictionary<string, string>
        {
            ["client_id"] = clientId ?? "dynamic_agent_client", ["ext_agent_host_id"] = hostId,
            ["response_type"] = "code", ["redirect_uri"] = callback.AbsoluteUri,
            ["scope"] = Scopes, ["resource"] = Resource, ["state"] = state, ["nonce"] = nonce,
            ["code_challenge_method"] = "S256", ["code_challenge"] = Challenge(verifier)
        };
        if (clientId is null) values["agent_name_hint"] = "LexiFlow";
        return new Uri(Issuer + "/api/accounts/authorize?" + string.Join("&", values.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value))));
    }

    public Task<ChatGptCredentials> ExchangeAsync(string clientId, string code, string verifier, Uri callback, CancellationToken ct)
        => TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = clientId, ["code"] = code,
            ["code_verifier"] = verifier, ["redirect_uri"] = callback.AbsoluteUri, ["resource"] = Resource
        }, ct);

    public Task<ChatGptCredentials> RefreshAsync(string clientId, string refreshToken, CancellationToken ct)
        => TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["client_id"] = clientId,
            ["refresh_token"] = refreshToken, ["resource"] = Resource
        }, ct);

    private async Task<ChatGptCredentials> TokenAsync(Dictionary<string, string> values, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint) { Content = new FormUrlEncodedContent(values) };
        using var response = await SendAsync(request, ct);
        using var body = await ReadJsonAsync(response, ct);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            if (GetString(body.RootElement, "error", 100) == "invalid_grant")
                throw new ChatGptException(ChatGptFailureKind.NotConnected);
            throw new ChatGptException(response.StatusCode == HttpStatusCode.TooManyRequests
                ? ChatGptFailureKind.UsageLimit : ChatGptFailureKind.Unavailable);
        }
        var root = body.RootElement;
        var access = GetString(root, "access_token", 32768);
        var refresh = GetString(root, "refresh_token", 32768);
        var identity = GetString(root, "id_token", 32768);
        var scope = GetString(root, "scope", 4096);
        if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(refresh) || string.IsNullOrWhiteSpace(identity)
            || string.IsNullOrWhiteSpace(scope) || access.Any(char.IsWhiteSpace) || refresh.Any(char.IsWhiteSpace)
            || !string.Equals(GetString(root, "token_type", 32), "Bearer", StringComparison.OrdinalIgnoreCase)
            || !root.TryGetProperty("expires_in", out var expires) || !expires.TryGetInt32(out var seconds) || seconds is < 1 or > 7200)
            throw new ChatGptException(ChatGptFailureKind.InvalidResponse);
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? earliest = null;
        if (root.TryGetProperty("earliest_refresh_at", out var earliestElement) && earliestElement.ValueKind != JsonValueKind.Null)
        {
            // Providers may represent this timestamp as Unix seconds or an ISO timestamp.
            if (earliestElement.ValueKind == JsonValueKind.Number && earliestElement.TryGetInt64(out var unix))
            {
                try { earliest = DateTimeOffset.FromUnixTimeSeconds(unix); }
                catch { throw new ChatGptException(ChatGptFailureKind.InvalidResponse); }
            }
            else if (earliestElement.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(earliestElement.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var timestamp)) earliest = timestamp;
            else throw new ChatGptException(ChatGptFailureKind.InvalidResponse);
            if (earliest > now.AddSeconds(seconds)) throw new ChatGptException(ChatGptFailureKind.InvalidResponse);
        }
        return new ChatGptCredentials
        {
            AccessToken = access, RefreshToken = refresh, IdToken = identity, Scope = scope,
            ExpiresAt = now.AddSeconds(seconds), SavedAt = now, EarliestRefreshAt = earliest
        };
    }

    public async Task<(string Subject, string? Email)> ValidateIdentityAsync(string token, string clientId, string? nonce,
        string? expectedSubject, CancellationToken ct)
    {
        if (!IsIssuedClientId(clientId) || token.Length > 32768) throw new ChatGptException(ChatGptFailureKind.InvalidResponse);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var keys = await GetKeysAsync(attempt != 0, ct);
            var result = await new JsonWebTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 32768 }.ValidateTokenAsync(token, new TokenValidationParameters
            {
                ValidIssuer = Issuer, ValidateIssuer = true, ValidAudience = clientId, ValidateAudience = true,
                IssuerSigningKeys = keys, ValidateIssuerSigningKey = true, RequireSignedTokens = true,
                RequireExpirationTime = true, ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(5),
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512,
                    SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.EcdsaSha512],
                IncludeTokenOnFailedValidation = false, SaveSigninToken = false
            });
            if (!result.IsValid)
            {
                if (attempt == 0 && result.Exception is SecurityTokenSignatureKeyNotFoundException) continue;
                throw new ChatGptException(ChatGptFailureKind.InvalidResponse);
            }
            if (result.SecurityToken is not JsonWebToken validated || string.IsNullOrWhiteSpace(validated.Subject) || validated.Subject.Length > 256
                || !validated.TryGetPayloadValue<long>("iat", out var issuedAt)
                || issuedAt > DateTimeOffset.UtcNow.AddSeconds(5).ToUnixTimeSeconds()
                || issuedAt > new DateTimeOffset(validated.ValidTo, TimeSpan.Zero).ToUnixTimeSeconds()
                || (nonce is not null && (!validated.TryGetPayloadValue<string>("nonce", out var actualNonce) || !FixedEquals(nonce, actualNonce)))
                || (expectedSubject is not null && !string.Equals(expectedSubject, validated.Subject, StringComparison.Ordinal)))
                throw new ChatGptException(ChatGptFailureKind.InvalidResponse);
            // OIDC authorized-party checks matter if an ID token has multiple audiences.
            var hasAuthorizedParty = validated.TryGetPayloadValue<string>("azp", out var authorizedParty);
            if ((hasAuthorizedParty && !string.Equals(authorizedParty, clientId, StringComparison.Ordinal))
                || (!hasAuthorizedParty && validated.Claims.Any(c => c.Type == "azp"))
                || (validated.Audiences.Count() > 1 && !hasAuthorizedParty))
                throw new ChatGptException(ChatGptFailureKind.InvalidResponse);
            var email = validated.TryGetPayloadValue<string>("email", out var claimEmail) && claimEmail.Length <= 254
                && !claimEmail.Any(char.IsControl) ? claimEmail : null;
            return (validated.Subject, email);
        }
        throw new ChatGptException(ChatGptFailureKind.InvalidResponse);
    }

    private async Task<IReadOnlyCollection<SecurityKey>> GetKeysAsync(bool force, CancellationToken ct)
    {
        if (!force && _keys is not null && DateTimeOffset.UtcNow - _keysReadAt < TimeSpan.FromMinutes(15)) return _keys;
        using var request = new HttpRequestMessage(HttpMethod.Get, Issuer + "/.well-known/jwks.json");
        using var response = await SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.OK) throw new ChatGptException(ChatGptFailureKind.Unavailable);
        using var json = await ReadJsonAsync(response, ct);
        try
        {
            var keys = new JsonWebKeySet(json.RootElement.GetRawText()).GetSigningKeys();
            if (keys.Count is < 1 or > 32) throw new InvalidDataException();
            _keys = keys.ToArray();
            _keysReadAt = DateTimeOffset.UtcNow;
            return _keys;
        }
        catch { throw new ChatGptException(ChatGptFailureKind.InvalidResponse); }
    }

    public async Task<bool> RevokeAsync(string clientId, string refreshToken, CancellationToken ct)
    {
        try
        {
            using var discoveryRequest = new HttpRequestMessage(HttpMethod.Get, Issuer + "/.well-known/openid-configuration");
            using var discoveryResponse = await SendAsync(discoveryRequest, ct);
            if (discoveryResponse.StatusCode != HttpStatusCode.OK) return false;
            using var discovery = await ReadJsonAsync(discoveryResponse, ct);
            var root = discovery.RootElement;
            if (GetString(root, "issuer", 200) != Issuer || !Uri.TryCreate(GetString(root, "revocation_endpoint", 500), UriKind.Absolute, out var endpoint)
                || endpoint.Scheme != "https" || endpoint.Host != "auth.openai.com" || !endpoint.IsDefaultPort
                || !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment)) return false;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["token"] = refreshToken, ["token_type_hint"] = "refresh_token", ["client_id"] = clientId
                    }) };
                    using var response = await SendAsync(request, ct);
                    if (response.StatusCode == HttpStatusCode.OK)
                        return (await ReadBytesAsync(response, ct)).Length == 0;
                    if ((int)response.StatusCode < 500) return false;
                }
                catch (ChatGptException) { if (attempt != 0) return false; }
                if (attempt == 0) await Task.Delay(350, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { }
        return false;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if ((int)response.StatusCode is >= 300 and < 400) { response.Dispose(); throw new ChatGptException(ChatGptFailureKind.InvalidResponse); }
            return response;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ChatGptException) { throw; }
        catch { throw new ChatGptException(ChatGptFailureKind.Unavailable); }
    }

    private static async Task<byte[]> ReadBytesAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            if (response.Content.Headers.ContentLength > ResponseLimit) throw new ChatGptException(ChatGptFailureKind.InvalidResponse);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var result = new MemoryStream();
            var buffer = new byte[4096];
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (result.Length + count > ResponseLimit) throw new ChatGptException(ChatGptFailureKind.InvalidResponse);
                result.Write(buffer, 0, count);
            }
            return result.ToArray();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ChatGptException) { throw; }
        catch { throw new ChatGptException(ChatGptFailureKind.Unavailable); }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try { return JsonDocument.Parse(await ReadBytesAsync(response, ct), new JsonDocumentOptions { MaxDepth = 16 }); }
        catch (ChatGptException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch { throw new ChatGptException(ChatGptFailureKind.InvalidResponse); }
    }

    private static string? GetString(JsonElement root, string name, int max) => root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString() is { } text && text.Length <= max ? text : null;
}
