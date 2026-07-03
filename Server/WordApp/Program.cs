using Microsoft.EntityFrameworkCore;
using System.Text;
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
builder.Services.AddHostedService<GrammarSyncService>();
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();