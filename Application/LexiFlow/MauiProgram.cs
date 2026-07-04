using LexiFlow.Services;
using LexiFlow.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using LexiFlow.ViewModels;
using Plugin.LocalNotification;

namespace LexiFlow
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseLocalNotification()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("RobotoSerif_28pt-Regular.ttf", "RobotoSerif");
                });

            builder.Services.AddSingleton<ApiService>();
            builder.Services.AddSingleton<SessionService>();
            builder.Services.AddSingleton<StreakService>();
            builder.Services.AddSingleton<NotificationService>();

            builder.Services.AddTransient<AppShell>();

            builder.Services.AddTransient<HomeView>();
            builder.Services.AddTransient<WordsView>();
            builder.Services.AddTransient<TestWordsView>();
            builder.Services.AddTransient<GrammarView>();
            builder.Services.AddTransient<TestGrammarView>();
            builder.Services.AddTransient<UserManageView>();
            builder.Services.AddTransient<AccountView>();

            builder.Services.AddTransient<WordsViewModel>();
            builder.Services.AddTransient<GrammarViewModel>();
            builder.Services.AddTransient<TestWordsViewModel>();
            builder.Services.AddTransient<UserManageViewModel>();
#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();

        }
    }
}