using System.Net.Http.Json;
using System.Net;
using System.Net.Http.Headers;
using LexiFlow.Models;

namespace LexiFlow.Services;

public class ApiService
{
    private readonly HttpClient _http;
    private readonly SessionService _session;

    public ApiService(SessionService session) : this(session, new HttpClient(new HttpClientHandler
    {
        // Do not bypass TLS validation, even in Debug; do not forward credentials on redirects.
        AllowAutoRedirect = false
    }) { BaseAddress = new Uri("https://lexiflow.duckdns.org/"), Timeout = TimeSpan.FromSeconds(15) }) { }

    internal ApiService(SessionService session, HttpClient http)
    {
        if (http.BaseAddress?.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("HTTPS is required.", nameof(http));
        _session = session;
        _http = http;
    }

    public async Task<List<Word>> GetWordsAsync() => await GetPublicAsync<List<Word>>("words") ?? [];
    public async Task<List<Grammar>> GetGrammarAsync() => await GetPublicAsync<List<Grammar>>("grammars") ?? [];
    public async Task<List<Idiom>> GetIdiomAsync() => await GetPublicAsync<List<Idiom>>("idioms") ?? [];

    private async Task<T?> GetPublicAsync<T>(string path)
    {
        using var response = await _http.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>();
    }

    private string OwnPath(string userId, string suffix)
    {
        if (!string.Equals(userId, _session.CurrentUserId, StringComparison.Ordinal))
            throw new InvalidOperationException("The requested account is not signed in.");
        return $"users/{Uri.EscapeDataString(userId)}/{suffix}";
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpRequestMessage request)
    {
        var token = _session.AccessToken;
        if (token is null)
        {
            _session.SignOut();
            throw new HttpRequestException("Please sign in again.", null, HttpStatusCode.Unauthorized);
        }
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Unauthorized) _session.Invalidate(token);
        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new HttpRequestException($"Request failed ({(int)status}).", null, status);
        }
        return response;
    }

    private async Task<List<T>> GetProgressAsync<T>(string userId, string suffix)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, OwnPath(userId, suffix));
        using var response = await SendAuthorizedAsync(request);
        return await response.Content.ReadFromJsonAsync<List<T>>() ?? [];
    }

    private async Task SaveProgressAsync(string userId, string suffix, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, OwnPath(userId, suffix)) { Content = JsonContent.Create(body) };
        using var response = await SendAuthorizedAsync(request);
    }

    public Task<List<WordProgress>> GetProgressAsync(string userId) => GetProgressAsync<WordProgress>(userId, "progress");
    public Task<List<GrammarProgress>> GetGrammarProgressAsync(string userId) => GetProgressAsync<GrammarProgress>(userId, "grammar-progress");
    public Task<List<IdiomProgress>> GetIdiomProgressAsync(string userId) => GetProgressAsync<IdiomProgress>(userId, "idiom-progress");
    public Task UpsertProgressAsync(string userId, string wordId, bool correct, string? status = null)
        => SaveProgressAsync(userId, "progress", new { WordId = wordId, Correct = correct, Status = status });
    public Task UpsertGrammarProgressAsync(string userId, string grammarId, bool correct, string? status = null)
        => SaveProgressAsync(userId, "grammar-progress", new { GrammarId = grammarId, Correct = correct, Status = status });
    public Task UpsertIdiomProgressAsync(string userId, string idiomId, bool correct, string? status = null)
        => SaveProgressAsync(userId, "idiom-progress", new { IdiomId = idiomId, Correct = correct, Status = status });

    public async Task<SessionCredentials?> LoginAsync(string userId, string pw)
    {
        using var response = await _http.PostAsJsonAsync("users/login", new { UserId = userId, Pw = pw });
        if (response.StatusCode == HttpStatusCode.Unauthorized) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SessionCredentials>();
    }

    public async Task<(bool ok, string? error)> SignUpAsync(string userId, string pw)
    {
        using var response = await _http.PostAsJsonAsync("users", new { UserId = userId, Pw = pw });
        if (response.IsSuccessStatusCode) return (true, null);
        return (false, response.StatusCode switch
        {
            HttpStatusCode.Conflict => "이미 사용 중인 아이디입니다.",
            HttpStatusCode.BadRequest => "아이디는 3~64자(문자·숫자·_·.·-), 비밀번호는 12자 이상·UTF-8 72바이트 이내로 입력해 주세요.",
            HttpStatusCode.TooManyRequests => "요청이 많습니다. 잠시 후 다시 시도해 주세요.",
            _ => "가입하지 못했습니다. 잠시 후 다시 시도해 주세요."
        });
    }

    public async Task LogoutAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "users/logout");
        using var response = await SendAuthorizedAsync(request);
    }

    public async Task ChangePasswordAsync(string currentPassword, string newPassword)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"users/{_session.CurrentAccountId}")
        { Content = JsonContent.Create(new { CurrentPw = currentPassword, Pw = newPassword }) };
        using var response = await SendAuthorizedAsync(request);
        _session.SignOut();
    }

    public async Task DeleteAccountAsync(string currentPassword)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"users/{_session.CurrentAccountId}")
        { Content = JsonContent.Create(new { CurrentPw = currentPassword }) };
        using var response = await SendAuthorizedAsync(request);
        // The caller clears this account's local data before ending its session.
    }
}
