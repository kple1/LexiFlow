using LexiFlow.Services;
using LexiFlow.Views;
using Microsoft.Extensions.DependencyInjection;

namespace LexiFlow
{
    public partial class App : Application
    {
        private readonly IServiceProvider _services;
        private readonly SessionService _session;
        private Window? _window;

        public App(IServiceProvider services, SessionService session)
        {
            InitializeComponent();
            _services = services;
            _session = session;
            _session.StateChanged += (_, _) => ApplyRootPage();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            _window = new Window
            {
                Width = 1024,
                Height = 550,
                // Shown briefly while the persisted session is restored.
                Page = new ContentPage
                {
                    BackgroundColor = Color.FromArgb("#222222"),
                    Content = new ActivityIndicator
                    {
                        IsRunning = true,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center
                    }
                }
            };

            var display = DeviceDisplay.MainDisplayInfo;
            double screenWidth = display.Width / display.Density;
            double screenHeight = display.Height / display.Density;
            _window.X = (screenWidth - _window.Width) / 2;
            _window.Y = (screenHeight - _window.Height) / 2;

            // Restore any saved session, then show the right root page.
            _window.Dispatcher.Dispatch(async () =>
            {
                await _session.RestoreAsync();
                ApplyRootPage();
            });

            return _window;
        }

        // Shows the tab shell when signed in, or the login screen when signed out.
        private void ApplyRootPage()
        {
            if (_window is null)
                return;

            _window.Page = _session.IsLoggedIn
                ? _services.GetRequiredService<AppShell>()
                : _services.GetRequiredService<UserManageView>();
        }
    }
}
