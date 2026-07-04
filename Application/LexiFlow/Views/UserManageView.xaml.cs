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

    private async void OnSignInClick(object sender, PointerEventArgs e)
    {
        var (ok, message) = await _vm.SignInAsync();
        // On success the session swaps the root page automatically; just show the message.
        if (!ok)
            await DisplayAlert("", message, "Confirm");
    }

    private async void OnSignUpClick(object sender, PointerEventArgs e)
    {
        var (_, message) = await _vm.SignUpAsync();
        await DisplayAlert("", message, "Confirm");
    }
}
