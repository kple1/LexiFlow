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
        };
    }
    public async Task<List<Word>> GetWordsAsync()
        => await _http.GetFromJsonAsync<List<Word>>("words") ?? [];

    public async Task<List<Grammar>> GetGrammarAsync()
        => await _http.GetFromJsonAsync<List<Grammar>>("grammar") ?? [];
}