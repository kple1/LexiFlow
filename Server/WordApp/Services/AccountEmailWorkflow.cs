using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WordApp.Auth;
using WordApp.Data;
using WordApp.Models;

namespace WordApp.Services;

public class AccountEmailWorkflow(AppDbContext db, IAccountEmailSender sender,
    IOptions<AccountEmailOptions> settings, ILogger<AccountEmailWorkflow> logger)
{
    public const string Signup = "signup";
    public const string Bind = "verify-email";
    public const string Reset = "reset-password";
    public const string Changed = "security-changed";
    public bool Enabled => settings.Value.Enabled && settings.Value.ValidOrigin && sender.IsConfigured;

    public async Task ProcessAsync(AccountMailRequest request, CancellationToken cancellation = default)
    {
        if (!Enabled || !RecoveryEmail.TryParse(request.Email, out var email, out var normalized)) return;
        if (request.Purpose is not (Signup or Bind or Reset or Changed)) return;
        var now = DateTime.UtcNow;
        await db.AccountActionTokens.Where(t => t.ExpiresAt <= now).ExecuteDeleteAsync(cancellation);
        await db.AccountMailBudgets.Where(b => b.ExpiresAt <= now).ExecuteDeleteAsync(cancellation);
        User? user = null;
        if (request.Purpose == Reset)
        {
            user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellation);
            if (user?.VerifiedEmail is null) return;
            email = user.VerifiedEmail; // Deliver to the verified address, never the lookup spelling.
        }
        else if (request.Purpose == Bind)
        {
            user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == request.UserId, cancellation);
            if (user is null || user.SecurityStamp != request.SecurityStamp) return;
            if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, cancellation)) return;
        }
        else if (request.Purpose == Signup && await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, cancellation)) return;

        if (!await SpendBudgetAsync(normalized, now, request.Purpose == Changed, cancellation)) return;
        if (request.Purpose == Changed)
        {
            await sender.SendAsync(email, "[LexiFlow] 계정 보안 설정이 변경되었습니다",
                "계정의 비밀번호 또는 복구 이메일이 변경되었습니다. 모든 기기에서 다시 로그인해야 합니다.\n본인이 요청하지 않았다면 즉시 비밀번호 찾기로 계정을 보호하고 운영자에게 문의하세요.\n이 메일에는 비밀번호나 인증 토큰이 없습니다.", cancellation);
            return;
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var lifetime = request.Purpose == Reset ? 15 : 30;
        var challenge = new AccountActionToken
        {
            TokenHash = SessionAuthenticationHandler.HashToken(token), Purpose = request.Purpose,
            UserId = user?.Id, SecurityStamp = user?.SecurityStamp, Email = email,
            NormalizedEmail = normalized, ExpiresAt = now.AddMinutes(lifetime)
        };
        db.AccountActionTokens.Add(challenge);
        await db.SaveChangesAsync(cancellation);
        // Fragment is not sent in an HTTP request, access log, or Referer header.
        var link = settings.Value.PublicBaseUrl.TrimEnd('/') + "/account/index.html#" + request.Purpose + "/" + token;
        var subject = request.Purpose switch
        {
            Signup => "[LexiFlow] 이메일 확인 후 가입을 완료하세요",
            Bind => "[LexiFlow] 복구 이메일을 확인하세요",
            _ => "[LexiFlow] 비밀번호 재설정"
        };
        try
        {
            await sender.SendAsync(email, subject,
                $"다음 링크에서 직접 확인 버튼을 눌러 요청을 완료하세요. 링크는 {lifetime}분 동안 한 번만 사용할 수 있습니다.\n\n{link}\n\n본인이 요청하지 않았다면 무시하세요. 링크를 다른 사람에게 전달하지 마세요. LexiFlow는 메일로 비밀번호를 요청하지 않습니다.", cancellation);
        }
        catch
        {
            // Even if SMTP acceptance was ambiguous, an undelivered/failed request
            // must not leave a usable bearer behind. Retry produces a fresh token.
            await db.AccountActionTokens.Where(t => t.TokenHash == challenge.TokenHash).ExecuteDeleteAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<bool> SpendBudgetAsync(string normalized, DateTime now, bool notification, CancellationToken cancellation)
    {
        var recipient = SessionAuthenticationHandler.HashToken(normalized);
        var budgets = new[]
        {
            ($"global-month:{now:yyyyMM}", 1000, new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1)),
            ($"global-day:{now:yyyyMMdd}", 50, now.Date.AddDays(1)),
            ($"{recipient}:day:{now:yyyyMMdd}", 5, now.Date.AddDays(1)),
            ($"{recipient}:hour:{now:yyyyMMddHH}", 3, now.AddHours(1)),
            ($"{recipient}:minute:{now:yyyyMMddHHmm}", 1, now.AddMinutes(2))
        };
        await using var transaction = await db.Database.BeginTransactionAsync(cancellation);
        try
        {
            // Security-change notices must not be swallowed by the one-minute
            // request cooldown. They still count toward global spend caps.
            foreach (var (key, limit, expiry) in notification ? budgets.Take(2) : budgets)
            {
                var exists = await db.AccountMailBudgets.AnyAsync(b => b.Key == key, cancellation);
                if (!exists)
                {
                    db.AccountMailBudgets.Add(new AccountMailBudget { Key = key, Count = 1, ExpiresAt = expiry });
                    await db.SaveChangesAsync(cancellation);
                }
                else if (await db.AccountMailBudgets.Where(b => b.Key == key && b.Count < limit)
                    .ExecuteUpdateAsync(update => update.SetProperty(b => b.Count, b => b.Count + 1), cancellation) == 0)
                {
                    logger.LogWarning("Account email attempt budget reached; request was not sent.");
                    return false; // Roll back every counter if any budget is exhausted.
                }
            }
            await transaction.CommitAsync(cancellation);
            return true;
        }
        catch (DbUpdateException)
        {
            // Concurrent first insert: fail closed rather than exceed the limit.
            return false;
        }
    }
}
