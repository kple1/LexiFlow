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