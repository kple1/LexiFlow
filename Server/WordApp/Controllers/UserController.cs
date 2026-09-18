using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WordApp.Auth;
using WordApp.Data;
using WordApp.Models;

namespace WordApp.Controllers;

[ApiController]
[Authorize]
[Route("users")]
public class UserController(AppDbContext db) : ControllerBase
{
    public record ChangePwDto(string CurrentPw, string Pw);
    public record CurrentPasswordDto(string CurrentPw);
    public record RegisterDto(string UserId, string Pw);
    public record LoginDto(string UserId, string Pw);

    [HttpGet("me")]
    public IActionResult Me() => Ok(new { Id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), UserId = User.Identity!.Name });

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (!AccountSecurity.IsOwnAccount(User, id)) return Forbid();
        var user = await db.Users.FindAsync(id);
        return user is null ? NotFound() : Ok(new { user.Id, user.UserId });
    }

    [AllowAnonymous]
    [EnableRateLimiting("credentials")]
    [HttpPost]
    public async Task<IActionResult> Post(RegisterDto dto)
    {
        if (!AccountSecurity.ValidNewUserId(dto.UserId) || !AccountSecurity.ValidNewPassword(dto.Pw))
            return BadRequest("ID: 3-64 letters, numbers, _, . or -. Password: at least 12 characters, at most 72 UTF-8 bytes.");
        if (await db.Users.AnyAsync(u => u.UserId == dto.UserId)) return Conflict("User ID already exists.");
        var user = new User { UserId = dto.UserId, Pw = BCrypt.Net.BCrypt.HashPassword(dto.Pw, 11) };
        db.Users.Add(user);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Conflict("User ID already exists."); }
        return CreatedAtAction(nameof(Get), new { id = user.Id }, new { user.Id, user.UserId });
    }

    [AllowAnonymous]
    [EnableRateLimiting("credentials")]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        if (string.IsNullOrEmpty(dto.UserId) || dto.UserId.Length > 100 || !AccountSecurity.ValidPasswordInput(dto.Pw))
            return Unauthorized();
        var now = DateTime.UtcNow;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.UserId == dto.UserId);
        var valid = AccountSecurity.Verify(dto.Pw, user?.Pw);
        if (user is null || user.LockoutUntil > now) return Unauthorized();
        if (!valid)
        {
            // Atomic update: parallel failures cannot overwrite one another's counters.
            var until = now.AddMinutes(15);
            await db.Users.Where(u => u.Id == user.Id && u.SecurityStamp == user.SecurityStamp)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(u => u.FailedLoginCount, u => u.LockoutUntil <= now ? 1 : u.FailedLoginCount + 1)
                    .SetProperty(u => u.LockoutUntil, u => u.LockoutUntil <= now ? null : u.FailedLoginCount >= 4 ? until : u.LockoutUntil));
            return Unauthorized();
        }
        var updated = await db.Users.Where(u => u.Id == user.Id && u.SecurityStamp == user.SecurityStamp
                && (u.LockoutUntil == null || u.LockoutUntil <= now))
            .ExecuteUpdateAsync(update => update.SetProperty(u => u.FailedLoginCount, 0).SetProperty(u => u.LockoutUntil, (DateTime?)null));
        if (updated == 0) return Unauthorized();

        await db.UserSessions.Where(s => s.UserId == user.Id && s.ExpiresAt <= now).ExecuteDeleteAsync();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiresAt = now.AddDays(7);
        db.UserSessions.Add(new UserSession
        {
            TokenHash = SessionAuthenticationHandler.HashToken(token), UserId = user.Id,
            SecurityStamp = user.SecurityStamp, ExpiresAt = expiresAt
        });
        await db.SaveChangesAsync();
        Response.Headers.CacheControl = "no-store";
        return Ok(new { user.Id, user.UserId, AccessToken = token, ExpiresAt = expiresAt });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var hash = User.FindFirstValue(SessionAuthenticationHandler.TokenClaim);
        await db.UserSessions.Where(s => s.TokenHash == hash).ExecuteDeleteAsync();
        return NoContent();
    }

    [EnableRateLimiting("credentials")]
    [HttpPatch("{id:int}")]
    public async Task<IActionResult> Patch(int id, ChangePwDto dto)
    {
        if (!AccountSecurity.IsOwnAccount(User, id)) return Forbid();
        if (!AccountSecurity.ValidNewPassword(dto.Pw)) return BadRequest("Password must be at least 12 characters and at most 72 UTF-8 bytes.");
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id);
        if (user is null || !AccountSecurity.Verify(dto.CurrentPw, user.Pw)) return Forbid();
        var hash = BCrypt.Net.BCrypt.HashPassword(dto.Pw, 11);
        var stamp = Guid.NewGuid().ToString("N");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var updated = await db.Users.Where(u => u.Id == id && u.SecurityStamp == user.SecurityStamp)
            .ExecuteUpdateAsync(update => update.SetProperty(u => u.Pw, hash)
                .SetProperty(u => u.SecurityStamp, stamp).SetProperty(u => u.FailedLoginCount, 0)
                .SetProperty(u => u.LockoutUntil, (DateTime?)null));
        if (updated == 0) return Unauthorized();
        await db.UserSessions.Where(s => s.UserId == id).ExecuteDeleteAsync();
        await transaction.CommitAsync();
        return NoContent();
    }

    [EnableRateLimiting("credentials")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CurrentPasswordDto dto)
    {
        if (!AccountSecurity.IsOwnAccount(User, id)) return Forbid();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id);
        if (user is null || !AccountSecurity.Verify(dto.CurrentPw, user.Pw)) return Forbid();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.WordProgresses.Where(p => p.UserId == user.UserId).ExecuteDeleteAsync();
        await db.GrammarProgresses.Where(p => p.UserId == user.UserId).ExecuteDeleteAsync();
        await db.IdiomProgresses.Where(p => p.UserId == user.UserId).ExecuteDeleteAsync();
        var deleted = await db.Users.Where(u => u.Id == id && u.SecurityStamp == user.SecurityStamp).ExecuteDeleteAsync();
        if (deleted == 0) return Unauthorized();
        // Sessions are removed by the database FK cascade.
        await transaction.CommitAsync();
        return NoContent();
    }
}
