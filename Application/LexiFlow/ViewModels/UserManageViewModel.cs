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
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

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
                return (false, "아이디 또는 비밀번호가 올바르지 않습니다.");

            await _session.SignInAsync(UserId);
            return (true, "로그인되었습니다.");
        }
        catch (Exception ex)
        {
            return (false, $"서버에 연결하지 못했습니다. 잠시 후 다시 시도해 주세요.\n{ex.Message}");
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
                ? (true, "가입이 완료되었습니다. 이제 로그인해 주세요.")
                : (false, apiError ?? "가입하지 못했습니다.");
        }
        catch (Exception ex)
        {
            return (false, $"서버에 연결하지 못했습니다. 잠시 후 다시 시도해 주세요.\n{ex.Message}");
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
            error = "아이디를 입력해 주세요.";
            return false;
        }

        if (string.IsNullOrEmpty(Password))
        {
            error = "비밀번호를 입력해 주세요.";
            return false;
        }

        error = "";
        return true;
    }
}
