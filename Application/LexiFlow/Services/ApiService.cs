using System.Net.Http.Json;
using LexiFlow.Models;

namespace LexiFlow.Services;

public class ApiService
{
    private readonly HttpClient _http;

    public ApiService()
    {
        var handler = new HttpClientHandler();

#if DEBUG
        handler.ServerCertificateCustomValidationCallback =
            (message, cert, chain, errors) => true;
#endif

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://34.158.213.95:5276/")
            //BaseAddress = new Uri("http://localhost:5276/")
        };
    }
    public async Task<List<Word>> GetWordsAsync()
        => await _http.GetFromJsonAsync<List<Word>>("words") ?? [];

    public async Task<List<Grammar>> GetGrammarAsync()
        => await _http.GetFromJsonAsync<List<Grammar>>("grammars") ?? [];

    // Per-user learning progress.
    public async Task<List<WordProgress>> GetProgressAsync(string userId)
        => await _http.GetFromJsonAsync<List<WordProgress>>($"users/{Uri.EscapeDataString(userId)}/progress") ?? [];

    // Upserts one word's progress after a review. Status transitions are decided by the caller.
    public async Task UpsertProgressAsync(string userId, string wordId, bool correct, string? status = null)
    {
        var body = new { WordId = wordId, Correct = correct, Status = status };
        var resp = await _http.PostAsJsonAsync($"users/{Uri.EscapeDataString(userId)}/progress", body);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<List<GrammarProgress>> GetGrammarProgressAsync(string userId)
        => await _http.GetFromJsonAsync<List<GrammarProgress>>($"users/{Uri.EscapeDataString(userId)}/grammar-progress") ?? [];

    public async Task UpsertGrammarProgressAsync(string userId, string grammarId, bool correct, string? status = null)
    {
        var body = new { GrammarId = grammarId, Correct = correct, Status = status };
        var resp = await _http.PostAsJsonAsync($"users/{Uri.EscapeDataString(userId)}/grammar-progress", body);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<List<Idiom>> GetIdiomAsync()
        => await _http.GetFromJsonAsync<List<Idiom>>("idioms") ?? [];

    public async Task<List<IdiomProgress>> GetIdiomProgressAsync(string userId)
        => await _http.GetFromJsonAsync<List<IdiomProgress>>($"users/{Uri.EscapeDataString(userId)}/idiom-progress") ?? [];

    public async Task UpsertIdiomProgressAsync(string userId, string idiomId, bool correct, string? status = null)
    {
        var body = new { IdiomId = idiomId, Correct = correct, Status = status };
        var resp = await _http.PostAsJsonAsync($"users/{Uri.EscapeDataString(userId)}/idiom-progress", body);
        resp.EnsureSuccessStatusCode();
    }

    // Sign in: server verifies the password (the hash is never sent to the client).
    public async Task<bool> LoginAsync(string userId, string pw)
    {
        var resp = await _http.PostAsJsonAsync("users/login", new { UserId = userId, Pw = pw });
        return resp.IsSuccessStatusCode;
    }

    // Sign up: returns (true, null) on success, or (false, message) on failure.
    public async Task<(bool ok, string? error)> SignUpAsync(string userId, string pw)
    {
        var resp = await _http.PostAsJsonAsync("users", new { UserId = userId, Pw = pw });
        if (resp.IsSuccessStatusCode)
            return (true, null);
        if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
            return (false, "User ID already exists.");
        return (false, $"Sign up failed (server returned {(int)resp.StatusCode}).");
    }
}