using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WordApp.Auth;
using WordApp.Data;
using WordApp.Models;
using WordApp.Services;

namespace WordApp.Controllers;

[ApiController]
[Route("account")]
[Authorize]
[EnableRateLimiting("account-email")]
public class AccountRecoveryController(AppDbContext db, AccountEmailQueue queue, AccountEmailWorkflow workflow) : ControllerBase
{
    public record EmailDto(string Email);
    public record BindDto(string Email, string CurrentPw);
    public record TokenDto(string Token);
    public record SignupDto(string Token, string UserId, string Pw);
    public record ResetDto(string Token, string Pw);
    private IActionResult Unavailable() => StatusCode(503, new { Message = "이메일 기능을 준비 중입니다. 기존 계정 로그인은 계속 사용할 수 있습니다." });
    private IActionResult AcceptedMessage() => Accepted(new { Message = "요청 가능한 주소라면 안내 메일을 보냅니다. 스팸함도 확인하고, 재요청은 잠시 후 시도해 주세요." });
    private IActionResult InvalidLink() => BadRequest(new { Message = "유효하지 않거나 만료된 링크입니다. 새 메일을 요청해 주세요." });

    [AllowAnonymous]
    [HttpGet("availability")]
    public IActionResult Availability() => Ok(new { Enabled = workflow.Enabled });

    [HttpGet("email")]
    public async Task<IActionResult> EmailStatus()
    {
        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id);
        return user is null ? Unauthorized() : Ok(new { Email = user.VerifiedEmail, Enabled = workflow.Enabled });
    }

    [AllowAnonymous]
    [HttpPost("signup/request")]
    public IActionResult RequestSignup(EmailDto dto) => RequestAnonymous(AccountEmailWorkflow.Signup, dto.Email);

    [AllowAnonymous]
    [HttpPost("password/request")]
    public IActionResult RequestReset(EmailDto dto) => RequestAnonymous(AccountEmailWorkflow.Reset, dto.Email);

    private IActionResult RequestAnonymous(string purpose, string address)
    {
        if (!workflow.Enabled) return Unavailable();
        // No account lookup or SMTP round trip in an anonymous HTTP request.
        // Unknown, throttled and known addresses receive identical responses.
        if (RecoveryEmail.TryParse(address, out var email, out _))
            queue.Enqueue(new AccountMailRequest(purpose, email));
        return AcceptedMessage();
    }

    [HttpPost("email/request")]
    public async Task<IActionResult> RequestEmail(BindDto dto)
    {
        if (!workflow.Enabled) return Unavailable();
        if (!RecoveryEmail.TryParse(dto.Email, out var email, out _)) return BadRequest(new { Message = "이메일 주소를 확인해 주세요." });
        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id);
        if (user is null || !AccountSecurity.Verify(dto.CurrentPw, user.Pw)) return Forbid();
        queue.Enqueue(new AccountMailRequest(AccountEmailWorkflow.Bind, email, id, user.SecurityStamp));
        return AcceptedMessage();
    }

    private async Task<AccountActionToken?> FindToken(string? token, string purpose)
    {
        if (token is not { Length: 64 } || !token.All(char.IsAsciiHexDigit)) return null;
        var hash = SessionAuthenticationHandler.HashToken(token);
        var now = DateTime.UtcNow;
        return await db.AccountActionTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash
            && t.Purpose == purpose && t.ExpiresAt > now);
    }

    private Task<int> Consume(AccountActionToken token) => db.AccountActionTokens
        .Where(t => t.TokenHash == token.TokenHash && t.Purpose == token.Purpose && t.ExpiresAt > DateTime.UtcNow).ExecuteDeleteAsync();

    [AllowAnonymous]
    [HttpPost("signup/complete")]
    public async Task<IActionResult> CompleteSignup(SignupDto dto)
    {
        if (!workflow.Enabled) return Unavailable();
        if (!AccountSecurity.ValidNewUserId(dto.UserId) || !AccountSecurity.ValidNewPassword(dto.Pw))
            return BadRequest(new { Message = "아이디는 3~64자(문자·숫자·_·.·-), 비밀번호는 12자 이상·UTF-8 72바이트 이내로 입력해 주세요." });
        var token = await FindToken(dto.Token, AccountEmailWorkflow.Signup);
        if (token is null || token.UserId is not null) return InvalidLink();
        var hash = BCrypt.Net.BCrypt.HashPassword(dto.Pw, 11);
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (await Consume(token) != 1) return InvalidLink();
        if (await db.Users.AnyAsync(u => u.UserId == dto.UserId)) return Conflict(new { Message = "이미 사용 중인 아이디입니다. 다른 아이디를 입력해 주세요." });
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == token.NormalizedEmail)) return InvalidLink();
        db.Users.Add(new User { UserId = dto.UserId, Pw = hash, VerifiedEmail = token.Email, NormalizedEmail = token.NormalizedEmail });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return InvalidLink(); }
        // The unique email constraint also makes older signup proofs unusable.
        // Do not lock/delete another signup transaction's proof here: it may be
        // waiting on this transaction's unique email insert (a lock cycle).
        // Unused proofs are removed by normal expiry cleanup.
        await transaction.CommitAsync();
        return Ok(new { Message = "이메일 인증과 가입이 완료되었습니다. 앱으로 돌아가 로그인해 주세요." });
    }

    [AllowAnonymous]
    [HttpPost("email/confirm")]
    public async Task<IActionResult> ConfirmEmail(TokenDto dto)
    {
        if (!workflow.Enabled) return Unavailable();
        var token = await FindToken(dto.Token, AccountEmailWorkflow.Bind);
        if (token?.UserId is null) return InvalidLink();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == token.UserId && u.SecurityStamp == token.SecurityStamp);
        if (user is null) return InvalidLink();
        var stamp = Guid.NewGuid().ToString("N");
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == token.NormalizedEmail && u.Id != user.Id)) return InvalidLink();
        int changed;
        try
        {
            changed = await db.Users.Where(u => u.Id == user.Id && u.SecurityStamp == token.SecurityStamp)
                .ExecuteUpdateAsync(update => update.SetProperty(u => u.VerifiedEmail, token.Email)
                    .SetProperty(u => u.NormalizedEmail, token.NormalizedEmail).SetProperty(u => u.SecurityStamp, stamp));
        }
        catch (Exception ex) when (ex is DbUpdateException || ex is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        { return InvalidLink(); }
        if (changed != 1) return InvalidLink();
        // Lock the account before its proofs, matching password changes/deletion.
        // Any failed consumption rolls the preceding update back atomically.
        if (await Consume(token) != 1) return InvalidLink();
        await Revoke(user.Id);
        await transaction.CommitAsync();
        if (user.VerifiedEmail is not null) queue.Enqueue(new AccountMailRequest(AccountEmailWorkflow.Changed, user.VerifiedEmail));
        return Ok(new { Message = "복구 이메일이 인증되었습니다. 모든 기기에서 다시 로그인해 주세요." });
    }

    [AllowAnonymous]
    [HttpPost("password/reset")]
    public async Task<IActionResult> ResetPassword(ResetDto dto)
    {
        if (!workflow.Enabled) return Unavailable();
        if (!AccountSecurity.ValidNewPassword(dto.Pw)) return BadRequest(new { Message = "비밀번호는 12자 이상·UTF-8 72바이트 이내로 입력해 주세요." });
        var token = await FindToken(dto.Token, AccountEmailWorkflow.Reset);
        if (token?.UserId is null) return InvalidLink();
        var hash = BCrypt.Net.BCrypt.HashPassword(dto.Pw, 11);
        var stamp = Guid.NewGuid().ToString("N");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var changed = await db.Users.Where(u => u.Id == token.UserId && u.SecurityStamp == token.SecurityStamp
            && u.NormalizedEmail == token.NormalizedEmail)
            .ExecuteUpdateAsync(update => update.SetProperty(u => u.Pw, hash).SetProperty(u => u.SecurityStamp, stamp)
                .SetProperty(u => u.FailedLoginCount, 0).SetProperty(u => u.LockoutUntil, (DateTime?)null));
        if (changed != 1) return InvalidLink();
        if (await Consume(token) != 1) return InvalidLink();
        await Revoke(token.UserId.Value);
        await transaction.CommitAsync();
        queue.Enqueue(new AccountMailRequest(AccountEmailWorkflow.Changed, token.Email));
        return Ok(new { Message = "비밀번호를 변경했습니다. 앱에서 새 비밀번호로 로그인해 주세요." });
    }

    private async Task Revoke(int userId)
    {
        await db.UserSessions.Where(s => s.UserId == userId).ExecuteDeleteAsync();
        await db.AccountActionTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync();
    }
}
