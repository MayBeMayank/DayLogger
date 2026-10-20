using Android.App;
using Android.Content;
using DayLogger.Services;
using Android.Content.PM;
using Android.OS;

namespace DayLogger
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            // Android 12+: wallpaper-based Material You colours for dialogs etc. Older: the static theme.
            Google.Android.Material.Color.DynamicColors.ApplyToActivityIfAvailable(this);
            base.OnCreate(savedInstanceState);
            Handle(Intent);
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            Handle(intent);
        }

        static void Handle(Intent? intent)
        {
            if (intent?.DataString == QuickAdd.ExpenseUri) QuickAdd.Request();
        }
    }
}
