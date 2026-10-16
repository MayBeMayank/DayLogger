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
