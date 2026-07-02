using LexiFlow.Views;
using Microsoft.Extensions.DependencyInjection;

namespace LexiFlow
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new AppShell())
            {
                Width = 1024,
                Height = 768
            };

            var display = DeviceDisplay.MainDisplayInfo;

            double screenWidth = display.Width / display.Density;
            double screenHeight = display.Height / display.Density;

            window.X = (screenWidth - window.Width) / 2;
            window.Y = (screenHeight - window.Height) / 2;

            return window;
        }

    }
}