namespace DayLogger.Services;

public enum ThemeMode { System, Light, Dark }

/// <summary>
/// Owns light/dark/system mode and fills the colour keys (Paper, Surface, Ink, Primary, ...) in the app
/// resources from the platform's own palette: Material You on Android 12+, iOS system colours, the
/// Windows accent. Everything in XAML and code references those keys via DynamicResource, so a
/// mode or wallpaper change re-colours the UI live.
/// </summary>
public static class ThemeService
{
    const string ModePref = "theme.mode";

    public static ThemeMode Mode
    {
        get => (ThemeMode)Preferences.Get(ModePref, (int)ThemeMode.System);
        set
        {
            Preferences.Set(ModePref, (int)value);
            Apply();
        }
    }

    public static void Init()
    {
        var app = Application.Current!;
        app.RequestedThemeChanged += (_, _) => Apply();
        Apply();
    }

    public static void Apply()
    {
        var app = Application.Current!;
        app.UserAppTheme = Mode switch
        {
            ThemeMode.Light => AppTheme.Light,
            ThemeMode.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };

        var effective = app.UserAppTheme != AppTheme.Unspecified ? app.UserAppTheme : app.PlatformAppTheme;
        var p = PlatformPalette.Get(effective == AppTheme.Dark);

        var res = app.Resources;
        res["Paper"] = p.Paper;
        res["Surface"] = p.Surface;
        res["Ink"] = p.Ink;
        res["Muted"] = p.Muted;
        res["Line"] = p.Line;
        res["Faint"] = p.Faint;
        res["Primary"] = p.Primary;
        res["OnPrimary"] = p.OnPrimary;
        res["Accent"] = p.Accent;
        res["PrimaryFill"] = p.Primary.WithAlpha(0.35f);
        res["PrimaryFillFull"] = p.Primary.WithAlpha(0.65f);
    }
}

public record Palette(Color Paper, Color Surface, Color Ink, Color Muted, Color Line, Color Faint, Color Primary, Color OnPrimary, Color Accent);

static class PlatformPalette
{
    static Color H(string hex) => Color.FromArgb(hex);

    static Color Mix(Color a, Color b, float t) => new(
        a.Red + (b.Red - a.Red) * t,
        a.Green + (b.Green - a.Green) * t,
        a.Blue + (b.Blue - a.Blue) * t);

    public static Palette Get(bool dark)
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(31)) return AndroidDynamic(dark);
        // Android 11 and older: no wallpaper colours, so a static yellow Material scheme.
        return dark
            ? new Palette(H("#141310"), H("#1E1C17"), H("#EAE6DC"), H("#B5AE9E"), H("#7C7566"), H("#3A372F"), H("#E8BC4A"), H("#261A00"), H("#4A3800"))
            : new Palette(H("#FFFFFF"), H("#F7F3EA"), H("#1C1B17"), H("#6B6558"), H("#8C8571"), H("#E0DACB"), H("#F0C04A"), H("#1C1B17"), H("#FFF3D1"));
#elif WINDOWS
        var ui = new Windows.UI.ViewManagement.UISettings();
        var a = ui.GetColorValue(dark ? Windows.UI.ViewManagement.UIColorType.AccentLight2 : Windows.UI.ViewManagement.UIColorType.Accent);
        var accent = Color.FromRgb(a.R, a.G, a.B);
        var paper = dark ? H("#202020") : H("#F3F3F3");
        return new Palette(
            paper,
            dark ? H("#2D2D2D") : H("#FFFFFF"),
            dark ? H("#FFFFFF") : H("#1A1A1A"),
            dark ? H("#C5C5C5") : H("#5D5D5D"),
            dark ? H("#6B6B6B") : H("#8A8A8A"),
            dark ? H("#3A3A3A") : H("#E5E5E5"),
            Mix(paper, accent, 0.8f), // eased towards the page colour so it is not harsh
            dark ? H("#000000") : H("#FFFFFF"),
            Mix(paper, accent, 0.18f));
#else
        // iOS / Mac Catalyst system colours.
        return dark
            ? new Palette(H("#000000"), H("#1C1C1E"), H("#FFFFFF"), H("#8E8E93"), H("#48484A"), H("#2C2C2E"), H("#5AA2F5"), H("#06203D"), H("#0A2A4D"))
            : new Palette(H("#FFFFFF"), H("#F2F2F7"), H("#000000"), H("#8E8E93"), H("#C6C6C8"), H("#E5E5EA"), H("#5B9BF0"), H("#FFFFFF"), H("#E5F0FF"));
#endif
    }

#if ANDROID
    // Material You: wallpaper-derived tonal palette, mapped like Material 3 does.
    static Palette AndroidDynamic(bool dark)
    {
        static Color C(int resId) => Color.FromInt(Platform.AppContext.GetColor(resId));
        return dark
            ? new Palette(
                Paper: C(Android.Resource.Color.SystemNeutral1900),
                Surface: C(Android.Resource.Color.SystemNeutral1800),
                Ink: C(Android.Resource.Color.SystemNeutral1100),
                Muted: C(Android.Resource.Color.SystemNeutral2200),
                Line: C(Android.Resource.Color.SystemNeutral2400),
                Faint: C(Android.Resource.Color.SystemNeutral2700),
                Primary: C(Android.Resource.Color.SystemAccent1300),
                OnPrimary: C(Android.Resource.Color.SystemAccent1900),
                Accent: C(Android.Resource.Color.SystemAccent1700))
            : new Palette(
                Paper: C(Android.Resource.Color.SystemNeutral110),
                Surface: C(Android.Resource.Color.SystemNeutral150),
                Ink: C(Android.Resource.Color.SystemNeutral1900),
                Muted: C(Android.Resource.Color.SystemNeutral2700),
                Line: C(Android.Resource.Color.SystemNeutral2500),
                Faint: C(Android.Resource.Color.SystemNeutral2200),
                Primary: C(Android.Resource.Color.SystemAccent1400),
                OnPrimary: C(Android.Resource.Color.SystemAccent1900),
                Accent: C(Android.Resource.Color.SystemAccent1100));
    }
#endif
}
