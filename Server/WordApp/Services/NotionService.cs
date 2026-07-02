using System.Text;
using System.Text.Json;
using WordApp.Models;

namespace WordApp.Services;

public class NotionService
{
    private readonly HttpClient _http;
    private readonly string _dataSourceId;

    public NotionService(HttpClient http, IConfiguration cfg)
    {
        _http = http;
        _dataSourceId = cfg["Notion:DataSourceId"]!;
        _http.BaseAddress = new Uri("https://api.notion.com/");
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {cfg["Notion:Token"]}");
        _http.DefaultRequestHeaders.Add("Notion-Version", "2025-09-03");
    }

    // Notion 전체 단어 가져오기 (페이지네이션 포함)
    public async Task<List<Word>> FetchAllAsync(CancellationToken ct = default)
    {
        var words = new List<Word>();
        string? cursor = null;

        do
        {
            var body = cursor is null
                ? "{\"page_size\":100}"
                : $"{{\"page_size\":100,\"start_cursor\":\"{cursor}\"}}";

            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"v1/data_sources/{_dataSourceId}/query");
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;

            foreach (var page in root.GetProperty("results").EnumerateArray())
                words.Add(Parse(page));

            cursor = root.GetProperty("has_more").GetBoolean()
                ? root.GetProperty("next_cursor").GetString()
                : null;
        }
        while (cursor is not null);

        return words;
    }

    private static Word Parse(JsonElement page)
    {
        var props = page.GetProperty("properties");
        return new Word
        {
            Id = page.GetProperty("id").GetString()!,
            English = Title(props, "English"),
            Meaning = Text(props, "Meaning"),
            Status = Select(props, "Status"),
            Example = Text(props, "Example"),
            Note = Text(props, "Note") is { Length: > 0 } n ? n : null,
            NotionCreated = page.GetProperty("created_time").GetDateTime()
        };
    }

    // title: 배열 비었으면 "" 반환
    private static string Title(JsonElement p, string name)
    {
        if (!p.TryGetProperty(name, out var prop)) return "";
        if (!prop.TryGetProperty("title", out var arr)) return "";
        foreach (var item in arr.EnumerateArray())
            if (item.TryGetProperty("plain_text", out var t))
                return t.GetString() ?? "";
        return "";
    }

    // rich_text: 배열 비었으면 "" 반환
    private static string Text(JsonElement p, string name)
    {
        if (!p.TryGetProperty(name, out var prop)) return "";
        if (!prop.TryGetProperty("rich_text", out var arr)) return "";
        foreach (var item in arr.EnumerateArray())
            if (item.TryGetProperty("plain_text", out var t))
                return t.GetString() ?? "";
        return "";
    }

    // select: null이면 "" 반환
    private static string Select(JsonElement p, string name)
    {
        if (!p.TryGetProperty(name, out var prop)) return "";
        if (!prop.TryGetProperty("select", out var sel)) return "";
        if (sel.ValueKind != JsonValueKind.Object) return "";   // select 값 없으면 null
        return sel.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
    }
}