using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;

namespace WordApp.Auth;

public static partial class AccountSecurity
{
    // An unknown account still does a password hash verification to reduce enumeration by timing.
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString(), 11);
    public static bool IsOwnAccount(ClaimsPrincipal principal, int id)
        => principal.FindFirstValue(ClaimTypes.NameIdentifier) == id.ToString();
    public static bool IsOwnProgress(ClaimsPrincipal principal, string userId)
        => string.Equals(principal.Identity?.Name, userId, StringComparison.Ordinal);

    public static bool ValidNewUserId(string? value)
        => value is not null && UserIdPattern().IsMatch(value);
    public static bool ValidPasswordInput(string? password)
        => !string.IsNullOrEmpty(password) && Encoding.UTF8.GetByteCount(password) <= 72;
    public static bool ValidNewPassword(string? password)
        => ValidPasswordInput(password) && password!.Length >= 12;
    public static bool Verify(string? password, string? hash)
    {
        if (!ValidPasswordInput(password)) return false;
        try { return BCrypt.Net.BCrypt.Verify(password!, hash ?? DummyHash) && hash is not null; }
        catch (BCrypt.Net.SaltParseException) { return false; }
    }

    [GeneratedRegex(@"\A[\p{L}\p{N}_.-]{3,64}\z")]
    private static partial Regex UserIdPattern();
}
