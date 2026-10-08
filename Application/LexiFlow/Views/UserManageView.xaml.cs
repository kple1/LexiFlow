using LexiFlow.ViewModels;
using LexiFlow.Services;

namespace LexiFlow.Views;

public partial class UserManageView : ContentPage
{
    private readonly UserManageViewModel _vm;

    public UserManageView(UserManageViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    private async void OnSignInClick(object? sender, EventArgs e)
        => await SignInAsync();

    private async void OnPasswordCompleted(object? sender, EventArgs e)
        => await SignInAsync();

    private async Task SignInAsync()
    {
        var (ok, message) = await _vm.SignInAsync();
        if (!ok)
            ShowMessage(message, success: false);
    }

    private async void OnSignUpClick(object? sender, EventArgs e)
    {
        await OpenAccountPageAsync("signup");
    }

    private async void OnForgotPasswordClick(object? sender, EventArgs e)
        => await OpenAccountPageAsync("forgot");

    private async Task OpenAccountPageAsync(string action)
    {
        try
        {
            await Browser.Default.OpenAsync(new Uri("https://lexiflow.duckdns.org/account/index.html#" + action), BrowserLaunchMode.External);
            ShowMessage("브라우저에서 진행한 뒤 이 앱으로 돌아와 로그인해 주세요.", true);
        }
        catch { ShowMessage("브라우저를 열지 못했습니다. 잠시 후 다시 시도해 주세요.", false); }
    }

    private void ShowMessage(string message, bool success)
    {
        messageCard.IsVisible = true;
        messageCard.BackgroundColor = ThemeColors.Get(success ? "SuccessSoft" : "DangerSoft");
        messageLabel.TextColor = ThemeColors.Get(success ? "Success" : "Danger");
        messageLabel.Text = message;
    }
}
