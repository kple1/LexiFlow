using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WordApp.Data;

namespace WordApp.Auth;

public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, AppDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "SessionBearer";
    public const string TokenClaim = "session_hash";

    public static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header)) return AuthenticateResult.NoResult();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.Fail("Bearer token required.");
        var token = header[7..];
        if (token.Length != 64 || token.Any(c => !char.IsAsciiHexDigit(c)))
            return AuthenticateResult.Fail("Invalid session.");

        var hash = HashToken(token);
        var session = await db.UserSessions.AsNoTracking().Include(s => s.User)
            .SingleOrDefaultAsync(s => s.TokenHash == hash, Context.RequestAborted);
        if (session is null || session.ExpiresAt <= DateTime.UtcNow
            || session.SecurityStamp != session.User.SecurityStamp)
            return AuthenticateResult.Fail("Session expired or revoked.");

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, session.User.Id.ToString()),
            new Claim(ClaimTypes.Name, session.User.UserId),
            new Claim(TokenClaim, hash)
        }, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
}
