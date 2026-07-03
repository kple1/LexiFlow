using LexiFlow.Services;

namespace LexiFlow.Views;

public partial class UserManageView : ContentPage
{
    private readonly ApiService _api;
    public UserManageView(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    private async void OnSignUpClick(object sender, PointerEventArgs e)
    {
        if (!ValidateInput())
            return;

        try
        {
            var (ok, error) = await _api.SignUpAsync(id.Text, pw.Text);
            if (!ok)
            {
                await DisplayAlert("", error ?? "Sign up failed.", "Confirm");
                return;
            }

            await DisplayAlert("", "Sign up complete.", "Confirm");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Could not reach the server.\n{ex.Message}", "Confirm");
        }
    }

    private async void OnSignInClick(object sender, PointerEventArgs e)
    {
        if (!ValidateInput())
            return;

        try
        {
            bool ok = await _api.LoginAsync(id.Text, pw.Text);
            if (!ok)
            {
                await DisplayAlert("", "Invalid user ID or password.", "Confirm");
                return;
            }

            await DisplayAlert("", "Sign in successful.", "Confirm");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Could not reach the server.\n{ex.Message}", "Confirm");
        }
    }

    private bool ValidateInput()
    {
        if (string.IsNullOrEmpty(id.Text))
        {
            _ = DisplayAlert("", "Please enter a user ID.", "Confirm");
            return false;
        }

        if (string.IsNullOrEmpty(pw.Text))
        {
            _ = DisplayAlert("", "Please enter a user PW.", "Confirm");
            return false;
        }

        return true;
    }
}