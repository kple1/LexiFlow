namespace LexiFlow.Services;

using System.Text.Json;
using LexiFlow.Models;

// The user id is display/context data, never proof of authentication.
public class SessionService
{
    private const string StorageKey = "session_credentials_v2";
    private SessionCredentials? _credentials;
    private string? _legacyOwner;
    public string? CurrentUserId => _credentials?.UserId;
    public int? CurrentAccountId => _credentials?.Id;
    public string StorageId => CurrentAccountId?.ToString() ?? "guest";
    public string? AccessToken => IsLoggedIn ? _credentials!.AccessToken : null;
    public bool IsLoggedIn => _credentials is { } value && value.ExpiresAt > DateTimeOffset.UtcNow;

    public event EventHandler? StateChanged;

    public async Task RestoreAsync()
    {
        _credentials = null;
        _legacyOwner = null;
        try
        {
            // Legacy id-only sessions deliberately do not grant access.
            _legacyOwner = await SecureStorage.GetAsync("session_user_id");
            var json = await SecureStorage.GetAsync(StorageKey);
            if (json is not null)
            {
                var value = JsonSerializer.Deserialize<SessionCredentials>(json);
                if (IsValid(value)) _credentials = value;
                else SecureStorage.Remove(StorageKey);
            }
        }
        catch { /* A missing/corrupt secure store means signed out. */ }
    }

    public async Task SignInAsync(SessionCredentials value)
    {
        if (!IsValid(value)) throw new InvalidOperationException("The server did not return a valid session.");
        LocalAccountData.MigrateLegacy(value.Id, value.UserId, _legacyOwner);
        _credentials = value;
        _legacyOwner = null;
        try { SecureStorage.Remove("session_user_id"); } catch { }
        try { await SecureStorage.SetAsync(StorageKey, JsonSerializer.Serialize(value)); }
        catch
        {
            // Do not retain an older account's session if saving the new one fails.
            try { SecureStorage.Remove(StorageKey); } catch { }
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Invalidate(string? rejectedToken)
    {
        // A delayed response from a previous login must not log out a new session.
        if (_credentials?.AccessToken == rejectedToken) SignOut();
    }

    public void SignOut()
    {
        _credentials = null;
        try { SecureStorage.Remove(StorageKey); SecureStorage.Remove("session_user_id"); }
        catch { }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsValid(SessionCredentials? value)
        => value is { Id: > 0 } && !string.IsNullOrWhiteSpace(value.UserId)
            && value.AccessToken is { Length: 64 } && value.AccessToken.All(char.IsAsciiHexDigit)
            && value.ExpiresAt > DateTimeOffset.UtcNow && value.ExpiresAt <= DateTimeOffset.UtcNow.AddDays(8);
}
