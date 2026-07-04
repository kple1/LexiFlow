namespace LexiFlow.Services;

// Holds the current sign-in state and persists it across app restarts.
// The login endpoint only returns success/failure (no token), so we persist
// the user id itself as the session key.
public class SessionService
{
    private const string UserIdKey = "session_user_id";

    public string? CurrentUserId { get; private set; }
    public bool IsLoggedIn => !string.IsNullOrEmpty(CurrentUserId);

    // Raised whenever the sign-in state changes so the app can swap its root page.
    public event EventHandler? StateChanged;

    // Loads any persisted session on startup. Safe to call before login.
    public async Task RestoreAsync()
    {
        try
        {
            CurrentUserId = await SecureStorage.GetAsync(UserIdKey);
        }
        catch
        {
            // SecureStorage can be unavailable on some platforms/configs; treat as logged out.
            CurrentUserId = null;
        }
    }

    public async Task SignInAsync(string userId)
    {
        CurrentUserId = userId;
        try
        {
            await SecureStorage.SetAsync(UserIdKey, userId);
        }
        catch
        {
            // Non-fatal: the session still works for this app run even if it can't persist.
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        CurrentUserId = null;
        try
        {
            SecureStorage.Remove(UserIdKey);
        }
        catch
        {
            // Ignore: nothing more we can do to clear it.
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
