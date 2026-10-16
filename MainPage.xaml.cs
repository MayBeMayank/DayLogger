using DayLogger.Services;

namespace DayLogger
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
            ShowThemeIcon();
        }

        void ShowThemeIcon() => ThemeIcon.Text = ThemeService.Mode switch
        {
            ThemeMode.Light => "☀️",
            ThemeMode.Dark => "🌙",
            _ => "🌗",
        };

        void OnThemeTapped(object? sender, TappedEventArgs e)
        {
            ThemeService.Mode = (ThemeMode)(((int)ThemeService.Mode + 1) % 3);
            ShowThemeIcon();
        }

        private async void OnMealsTapped(object? sender, TappedEventArgs e) =>
            await Shell.Current.GoToAsync("meals");

        private async void OnExpensesTapped(object? sender, TappedEventArgs e) =>
            await Shell.Current.GoToAsync("expenses");

        private async void OnRemindersTapped(object? sender, TappedEventArgs e) =>
            await Shell.Current.GoToAsync("reminders");

        private async void OnBackupTapped(object? sender, TappedEventArgs e) =>
            await Shell.Current.GoToAsync("backup");

        private async void OnStreaksTapped(object? sender, TappedEventArgs e) =>
            await Shell.Current.GoToAsync("streaks");
    }
}
