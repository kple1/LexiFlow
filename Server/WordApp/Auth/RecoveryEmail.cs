using System.Text.RegularExpressions;

namespace WordApp.Auth;

public static partial class RecoveryEmail
{
    // Explicit product policy: ASCII addresses, case-insensitive uniqueness. Keep
    // original spelling for delivery. Never remove dots/plus tags or guess aliases.
    public static bool TryParse(string? value, out string email, out string normalized)
    {
        email = value?.Trim() ?? "";
        normalized = "";
        if (email.Length is < 3 or > 254 || !EmailPattern().IsMatch(email)) return false;
        var local = email[..email.IndexOf('@')];
        if (local.Length > 64 || local.StartsWith('.') || local.EndsWith('.') || local.Contains("..")) return false;
        normalized = email.ToUpperInvariant();
        return true;
    }

    [GeneratedRegex(@"\A[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+\z")]
    private static partial Regex EmailPattern();
}
