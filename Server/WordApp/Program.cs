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
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHttpClient<NotionService>();
if (!builder.Environment.IsEnvironment("Testing") && builder.Configuration.GetValue("Notion:SyncEnabled", true))
    builder.Services.AddHostedService<WordSyncService>();
builder.Services.AddScoped<AdminAuthFilter>();
builder.AddApiSecurity();
var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    for (var i = 10; i > 0; i--)
    {
        try
        {
            if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations") || app.Environment.IsDevelopment())
                db.Database.Migrate();
            else if (db.Database.GetPendingMigrations().Any())
                throw new InvalidOperationException("Pending schema changes: back up the database and explicitly enable Database:ApplyMigrations.");
            break;
        }
        catch (Exception ex) when (i > 1)
        {
            app.Logger.LogWarning("DB 연결 대기중, {left}회 남음: {msg}", i - 1, ex.Message);
            Thread.Sleep(5000);
        }
    }
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseForwardedHeaders();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/users") || context.Request.Path.StartsWithSegments("/admin"))
        context.Response.Headers.CacheControl = "no-store";
    // Fail closed instead of redirecting a request that might already contain credentials.
    if (!context.Request.IsHttps)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("HTTPS is required.");
        return;
    }
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { Status = "ok" })).AllowAnonymous();
app.Run();

public partial class Program { }
