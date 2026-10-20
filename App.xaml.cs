using DayLogger.Pages;
using DayLogger.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DayLogger
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            ThemeService.Init();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            // Android destroys and recreates the activity (e.g. coming back from another app) while the process lives on, and
            // asks for a window each time. Hand back the one that exists, so the user returns to the page they left, not the splash.
            if (Windows.Count > 0) return Windows[0];

            var splash = new SplashPage();
            var window = new Window(splash);
            splash.Finished += (_, _) => window.Page = new AppShell();

            // Re-plan reminders on every launch: heals a broken alarm chain and is what drives Windows timers.
            var reminders = IPlatformApplication.Current?.Services.GetService<ReminderService>();
            if (reminders != null) _ = Task.Run(reminders.RefreshAllAsync);
            return window;
        }
    }
}
