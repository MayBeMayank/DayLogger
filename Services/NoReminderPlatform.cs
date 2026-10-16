namespace DayLogger.Services;

/// <summary>iOS and Mac Catalyst: reminders aren't implemented there yet, so scheduling does nothing.</summary>
public class NoReminderPlatform : IReminderPlatform
{
    public Func<string, Task>? Fired { get; set; }
    public Task<bool> RequestPermissionAsync() => Task.FromResult(false);
    public void Schedule(string key, DateTime at) { }
    public void Cancel(string key) { }
    public void Show(string key, string title, string body) { }
}
