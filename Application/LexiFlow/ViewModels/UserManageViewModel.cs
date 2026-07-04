using CommunityToolkit.Mvvm.ComponentModel;
using LexiFlow.Services;

namespace LexiFlow.ViewModels;

public partial class UserManageViewModel : ObservableObject
{
    private readonly ApiService _api;
    private readonly SessionService _session;

    public UserManageViewModel(ApiService api, SessionService session)
    {
        _api = api;
        _session = session;
    }

    [ObservableProperty]
    private string _userId = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private bool _isBusy;

    // Signs in and, on success, persists the session (which swaps the app to the tabs).
    // Returns a message for the view to surface to the user.
    public async Task<(bool ok, string message)> SignInAsync()
    {
        if (!Validate(out var error))
            return (false, error);

        try
        {
            IsBusy = true;
            bool ok = await _api.LoginAsync(UserId, Password);
            if (!ok)
                return (false, "Invalid user ID or password.");

            await _session.SignInAsync(UserId);
            return (true, "Sign in successful.");
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach the server.\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<(bool ok, string message)> SignUpAsync()
    {
        if (!Validate(out var error))
            return (false, error);

        try
        {
            IsBusy = true;
            var (ok, apiError) = await _api.SignUpAsync(UserId, Password);
            return ok
                ? (true, "Sign up complete. You can now sign in.")
                : (false, apiError ?? "Sign up failed.");
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach the server.\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool Validate(out string error)
    {
        if (string.IsNullOrEmpty(UserId))
        {
            error = "Please enter a user ID.";
            return false;
        }

        if (string.IsNullOrEmpty(Password))
        {
            error = "Please enter a password.";
            return false;
        }

        error = "";
        return true;
    }
}
