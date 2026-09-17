using LexiFlow.ViewModels;

namespace LexiFlow.Views;

public partial class UserManageView : ContentPage
{
    private readonly UserManageViewModel _vm;

    public UserManageView(UserManageViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    private async void OnSignInClick(object sender, EventArgs e)
        => await SignInAsync();

    private async void OnPasswordCompleted(object sender, EventArgs e)
        => await SignInAsync();

    private async Task SignInAsync()
    {
        var (ok, message) = await _vm.SignInAsync();
        if (!ok)
            ShowMessage(message, success: false);
    }

    private async void OnSignUpClick(object sender, EventArgs e)
    {
        var (ok, message) = await _vm.SignUpAsync();
        ShowMessage(message, ok);
    }

    private void ShowMessage(string message, bool success)
    {
        messageCard.IsVisible = true;
        messageCard.BackgroundColor = Color.FromArgb(success ? "#163A2A" : "#45202B");
        messageLabel.TextColor = Color.FromArgb(success ? "#86EFAC" : "#FDA4AF");
        messageLabel.Text = message;
    }
}
