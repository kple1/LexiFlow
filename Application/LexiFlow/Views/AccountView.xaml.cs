using LexiFlow.Services;

namespace LexiFlow.Views;

public partial class AccountView : ContentPage
{
    private readonly SessionService _session;
    private readonly ApiService _api;

    public AccountView(SessionService session, ApiService api)
    {
        InitializeComponent();
        _session = session;
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        userLabel.Text = _session.CurrentUserId ?? "-";
        await LoadStatsAsync();
    }

    private async Task LoadStatsAsync()
    {
        if (!_session.IsLoggedIn)
            return;

        try
        {
            var progress = await _api.GetProgressAsync(_session.CurrentUserId!);
            masteredLabel.Text = progress.Count(p => p.Status == "Mastered").ToString();
            learningLabel.Text = progress.Count(p => p.Status == "Learning").ToString();
            totalLabel.Text = progress.Count.ToString();
        }
        catch
        {
            // Server/progress endpoint may be unavailable; leave the zeros in place.
        }
    }

    private async void OnSignOutClick(object sender, EventArgs e)
    {
        bool confirm = await DisplayAlert("Sign out", "Do you want to sign out?", "Sign out", "Cancel");
        if (confirm)
            _session.SignOut(); // swaps the root page back to the login screen
    }
}
