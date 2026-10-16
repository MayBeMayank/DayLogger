using System.Runtime.CompilerServices;

namespace DayLogger.Controls;

/// <summary>
/// Metro theme transitions (see DESIGN.md "Motion"): list items fly in one after another (continuum / entrance) and a
/// new page's elements flip in from the left edge like a turning page (turnstile). Both are staggered, never together.
/// </summary>
public static class Motion
{
    const int Step = 30;          // ms between consecutive items
    const int TurnStep = 40;      // ms between consecutive flipping elements
    const int MaxItems = 14;      // later items just appear, so a long list never keeps the user waiting
    const uint FlyMs = 260;
    const uint FlipMs = 300;
    const double FlyOffset = 56;  // dp the items travel in from the right
    const double FlipAngle = -60; // degrees the elements start turned away

    /// <summary>True when the system asks for no animation; parallax and staggering are then skipped.</summary>
    public static bool Reduced
    {
        get
        {
#if ANDROID
            try { return Android.Provider.Settings.Global.GetFloat(Android.App.Application.Context.ContentResolver, Android.Provider.Settings.Global.AnimatorDurationScale, 1f) == 0f; }
            catch { return false; }
#elif WINDOWS
            try { return !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled; }
            catch { return false; }
#else
            return false;
#endif
        }
    }

    // Every run claims its views with a token. A newer run (or Hide) takes the views over, and the older one then neither
    // starts its delayed steps nor settles the views, so interrupted animations can't leave an item part-way across.
    static readonly ConditionalWeakTable<View, object> Runs = new();
    static readonly ConditionalWeakTable<View, StrongBox<double>> Anchors = new();

    static object Claim(View v)
    {
        var token = new object();
        Runs.AddOrUpdate(v, token);
        v.CancelAnimations();
        return token;
    }

    static int _active;

    /// <summary>True while an entrance is running. Continuous animations (the liquid fills) hold still meanwhile so the frame budget goes to the entrance.</summary>
    public static bool Busy => _active > 0;

    // While a view flies or flips, Android draws it once into a GPU layer and moves that, instead of redrawing its text
    // and children every frame: removes the flicker on big text and the lag on heavy views such as the calendar.
    // Skipped for very large views, which would not fit in a texture.
    static void Layer(View v, bool on)
    {
#if ANDROID
        if (v.Handler?.PlatformView is Android.Views.View pv && (!on || (pv.Width < 3000 && pv.Height < 3000)))
            pv.SetLayerType(on ? Android.Views.LayerType.Hardware : Android.Views.LayerType.None, null);
#endif
    }

    static bool Owns(View v, object token) => Runs.TryGetValue(v, out var t) && ReferenceEquals(t, token);

    /// <summary>
    /// Hides the items off to one side so <see cref="FlyInAsync"/> can reveal them without a flash of the final layout.
    /// <paramref name="from"/> is 1 for the right edge, -1 for the left.
    /// </summary>
    public static void Hide(IEnumerable<View> items, int from = 1)
    {
        if (Reduced) return;
        foreach (var v in items.Take(MaxItems)) { Claim(v); v.Opacity = 0; v.TranslationX = FlyOffset * from; }
    }

    /// <summary>Items slide in from the right (<paramref name="from"/> 1) or the left (-1) and fade up, each a beat after the one before.</summary>
    public static async Task FlyInAsync(IEnumerable<View> items, int from = 1)
    {
        var list = items.Take(MaxItems).ToList();
        if (Reduced)
        {
            foreach (var v in list) { v.Opacity = 1; v.TranslationX = 0; }
            return;
        }

        var runs = list.Select(v => (View: v, Token: Claim(v))).ToList();
        foreach (var (v, _) in runs) { v.Opacity = 0; v.TranslationX = FlyOffset * from; Layer(v, true); }
        _active++;
        try
        {
            await Task.WhenAll(runs.Select((r, i) => FlyOneAsync(r.View, r.Token, i * Step)));
        }
        finally
        {
            _active--;
            foreach (var (v, token) in runs)
                if (Owns(v, token)) { v.Opacity = 1; v.TranslationX = 0; Layer(v, false); }
        }
    }

    static async Task FlyOneAsync(View v, object token, int delay)
    {
        if (delay > 0) await Task.Delay(delay);
        if (!Owns(v, token)) return;
        await Task.WhenAll(
            v.FadeToAsync(1, FlyMs, Easing.CubicOut),
            v.TranslateToAsync(0, 0, FlyMs, Easing.CubicOut));
    }

    /// <summary>
    /// Elements swing in around their left edge, one after another, like pages of a book turning. Going forward they
    /// start turned one way, coming back (<paramref name="forward"/> false) the other, so the two directions differ.
    /// The elements are hidden synchronously, before the first await.
    /// </summary>
    public static async Task TurnstileInAsync(IEnumerable<View> items, bool forward = true)
    {
        if (Reduced) return;

        var runs = items.Take(MaxItems).Select(v => (View: v, Token: Claim(v))).ToList();
        foreach (var (v, _) in runs)
        {
            Anchors.GetValue(v, k => new StrongBox<double>(k.AnchorX)); // first run remembers the real anchor
            v.AnchorX = 0; v.Opacity = 0; v.RotationY = forward ? FlipAngle : -FlipAngle;
            Layer(v, true);
        }
        _active++;
        try
        {
            await Task.WhenAll(runs.Select((r, i) => TurnOneAsync(r.View, r.Token, i * TurnStep)));
        }
        finally
        {
            _active--;
            foreach (var (v, token) in runs)
            {
                if (!Owns(v, token)) continue;
                Layer(v, false);
                v.Opacity = 1; v.RotationY = 0;
                v.AnchorX = Anchors.TryGetValue(v, out var box) ? box.Value : 0.5;
                Anchors.Remove(v);
            }
        }
    }

    static async Task TurnOneAsync(View v, object token, int delay)
    {
        if (delay > 0) await Task.Delay(delay);
        if (!Owns(v, token)) return;
        await Task.WhenAll(
            v.FadeToAsync(1, FlipMs, Easing.CubicOut),
            v.RotateYToAsync(0, FlipMs, Easing.CubicOut));
    }
}
