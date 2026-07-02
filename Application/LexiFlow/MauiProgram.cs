using LexiFlow.Services;
using LexiFlow.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace LexiFlow
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("RobotoSerif_28pt-Regular.ttf", "RobotoSerif");
                });
            builder.Services.AddSingleton<ApiService>();
            builder.Services.AddTransient<DailyWords>();
            builder.Services.AddTransient<TestWords>();
#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();

        }
    }
}