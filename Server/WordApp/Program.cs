using Microsoft.EntityFrameworkCore;
using System.Text;
using WordApp.Auth;
using WordApp.Data;
using WordApp.Services;

var builder = WebApplication.CreateBuilder(args);
var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
if (string.IsNullOrEmpty(urls))
    builder.WebHost.UseUrls("https://0.0.0.0:7299");

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHttpClient<NotionService>();
builder.Services.AddHostedService<WordSyncService>();
builder.Services.AddScoped<AdminAuthFilter>();
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    for (var i = 10; i > 0; i--)
    {
        try { db.Database.Migrate(); break; }
        catch (Exception ex) when (i > 1)
        {
            app.Logger.LogWarning("DB 연결 대기중, {left}회 남음: {msg}", i - 1, ex.Message);
            Thread.Sleep(5000);
        }
    }
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthorization();
app.MapControllers();
app.Run();