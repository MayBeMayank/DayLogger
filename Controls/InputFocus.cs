namespace DayLogger.Controls;

public static class InputFocus
{
    /// <summary>
    /// Focuses an entry and brings up the keyboard once the page has finished appearing. Calling Focus() straight from
    /// OnAppearing is too early on Android (the page is still sliding in and has no window focus), so nothing happens.
    /// </summary>
    public static async void FocusSoon(this Entry entry)
    {
        try
        {
            await Task.Delay(400);
            if (entry.Handler == null) return;
            entry.Focus();

#if ANDROID
            // Focus() alone doesn't always raise the keyboard; ask for it once the view really holds focus.
            await Task.Delay(150);
            if (entry.Handler?.PlatformView is Android.Views.View view && view.HasFocus)
            {
                var input = view.Context?.GetSystemService(Android.Content.Context.InputMethodService) as Android.Views.InputMethods.InputMethodManager;
                input?.ShowSoftInput(view, Android.Views.InputMethods.ShowFlags.Implicit);
            }
#endif
        }
        catch
        {
            // Focus is a convenience; never let it crash a page.
        }
    }
}
