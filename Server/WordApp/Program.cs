using Microsoft.EntityFrameworkCore;
using System.Text;
using WordApp.Data;
using WordApp.Services;

var builder = WebApplication.CreateBuilder(args);
var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
if (string.IsNullOrEmpty(urls))
{
    builder.WebHost.UseUrls("https://0.0.0.0:7299");  // 로컬 개발용
}
// 환경변수 있으면(=Docker) 그걸 자동으로 씀 — UseUrls 호출 안 함builder.WebHost.UseUrls("https://0.0.0.0:7299");
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHttpClient<NotionService>();
builder.Services.AddHostedService<WordSyncService>();
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

//using (var http = new HttpClient())
//{
//    http.DefaultRequestHeaders.Add("Authorization",
//        $"Bearer {builder.Configuration["Notion:Token"]}");
//    http.DefaultRequestHeaders.Add("Notion-Version", "2025-09-03");

//    var dsId = builder.Configuration["Notion:DataSourceId"];
//    var url = $"https://api.notion.com/v1/data_sources/{dsId}/query";
//    Console.WriteLine($"URL: {url}");   // ← 실제 URL 찍어서 눈으로 확인

//    var res = await http.PostAsync(url,
//        new StringContent("{\"page_size\":1}", Encoding.UTF8, "application/json"));

//    Console.WriteLine(await res.Content.ReadAsStringAsync());
//}