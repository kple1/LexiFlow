using Microsoft.EntityFrameworkCore;
using System.Text;
using WordApp.Data;
using WordApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHttpClient<NotionService>();
builder.Services.AddHostedService<WordSyncService>();
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

using (var http = new HttpClient())
{
    http.DefaultRequestHeaders.Add("Authorization",
        $"Bearer {builder.Configuration["Notion:Token"]}");
    http.DefaultRequestHeaders.Add("Notion-Version", "2025-09-03");

    var dsId = builder.Configuration["Notion:DataSourceId"];
    var url = $"https://api.notion.com/v1/data_sources/{dsId}/query";
    Console.WriteLine($"URL: {url}");   // ← 실제 URL 찍어서 눈으로 확인

    var res = await http.PostAsync(url,
        new StringContent("{\"page_size\":1}", Encoding.UTF8, "application/json"));

    Console.WriteLine(await res.Content.ReadAsStringAsync());
}