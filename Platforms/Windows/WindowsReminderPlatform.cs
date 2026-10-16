using DayLogger.Services;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace DayLogger;

/// <summary>
/// The Windows app is unpackaged, so nothing can wake it: reminders fire from in-process timers and only while the app
/// is running (minimised to the taskbar counts).
/// </summary>
public class WindowsReminderPlatform : IReminderPlatform
{
    readonly Dictionary<string, Timer> _timers = new();
    readonly object _gate = new();
    bool _registered;

    public Func<string, Task>? Fired { get; set; }

    public Task<bool> RequestPermissionAsync() => Task.FromResult(true);

    public void Schedule(string key, DateTime at)
    {
        var due = at - DateTime.Now;
        if (due < TimeSpan.Zero) due = TimeSpan.Zero;
        lock (_gate)
        {
            _timers.GetValueOrDefault(key)?.Dispose();
            _timers[key] = new Timer(_ => { _ = Fired?.Invoke(key); }, null, due, Timeout.InfiniteTimeSpan);
        }
    }

    public void Cancel(string key)
    {
        lock (_gate)
        {
            if (_timers.Remove(key, out var t)) t.Dispose();
        }
    }

    public void Show(string key, string title, string body)
    {
        try
        {
            if (!_registered)
            {
                AppNotificationManager.Default.Register();
                _registered = true;
            }
            AppNotificationManager.Default.Show(new AppNotificationBuilder().AddText(title).AddText(body).BuildNotification());
        }
        catch
        {
            // Toasts can be unavailable for unpackaged apps (focus assist, policy); the reminder is simply lost.
        }
    }
}
