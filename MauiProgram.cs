using DayLogger.Data;
using DayLogger.Pages;
using DayLogger.Services;
using Microsoft.Extensions.Logging;

namespace DayLogger
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder.UseMauiApp<App>();
#if ANDROID || WINDOWS
            builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<Controls.SwipeHost, SwipeHostHandler>());
#endif

            builder.Services.AddSingleton<CategoryRepository>();
            builder.Services.AddSingleton<StreakRepository>();
            builder.Services.AddSingleton<ExpenseRepository>();
            builder.Services.AddSingleton<PersonRepository>();
            builder.Services.AddSingleton<BackupService>();
#if ANDROID
            builder.Services.AddSingleton<IReminderPlatform, AndroidReminderPlatform>();
#elif WINDOWS
            builder.Services.AddSingleton<IReminderPlatform, WindowsReminderPlatform>();
#else
            builder.Services.AddSingleton<IReminderPlatform, NoReminderPlatform>();
#endif
            builder.Services.AddSingleton<ReminderService>();
            builder.Services.AddTransient<RemindersPage>();
            builder.Services.AddTransient<BackupPage>();
            builder.Services.AddTransient<ExpensesCalendarPage>();
            builder.Services.AddTransient<ExpenseDayPage>();
            builder.Services.AddTransient<CategoryPage>();
            builder.Services.AddTransient<PeoplePage>();
            builder.Services.AddTransient<PersonPage>();
            builder.Services.AddTransient<StreaksPage>();
            builder.Services.AddTransient<StreakDetailPage>();
            builder.Services.AddTransient<StreakBinPage>();

            // Entries sit inside our own bordered boxes, so drop the native underline/box.
            Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("Borderless", (handler, view) =>
            {
#if ANDROID
                handler.PlatformView.Background = null;
                handler.PlatformView.SetPadding(0, 0, 0, 0);
#elif WINDOWS
                handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
                handler.PlatformView.Background = null;
#endif
            });

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
