using DayLogger.Data;
using DayLogger.Models;

namespace DayLogger.Services;

/// <summary>What the OS side of a reminder has to do. One pending alarm per key, always the next occurrence.</summary>
public interface IReminderPlatform
{
    /// <summary>Raised by platforms that fire in-process (Windows). Android fires through its broadcast receiver instead.</summary>
    Func<string, Task>? Fired { get; set; }

    Task<bool> RequestPermissionAsync();
    void Schedule(string key, DateTime at);
    void Cancel(string key);
    void Show(string key, string title, string body);
}

/// <summary>
/// Local reminders for streaks and expense categories. Each reminder key ("streak:5", "cat:2") holds a single
/// pending alarm for its next time; when it fires, the message is decided from today's data (nothing is shown if the
/// thing is already done) and the following alarm is scheduled.
/// </summary>
public class ReminderService
{
    readonly IReminderPlatform _platform;
    readonly StreakRepository _streaks;
    readonly CategoryRepository _categories;
    readonly ExpenseRepository _expenses;

    // Alarm keys from before categories existed; cancelled so none keeps firing after the update.
    static readonly string[] LegacyKeys = { "meal:0", "meal:1", "meal:2", "expense" };

    public ReminderService(IReminderPlatform platform, StreakRepository streaks, CategoryRepository categories, ExpenseRepository expenses)
    {
        _platform = platform;
        _streaks = streaks;
        _categories = categories;
        _expenses = expenses;
        _platform.Fired = HandleFiredAsync;
    }

    /// <summary>Minutes after midnight of each reminder in a day: first at the window start, last at its end, evenly between.</summary>
    public static IReadOnlyList<int> Slots(Streak s)
    {
        int n = Math.Min(s.ReminderCount, s.Frequency);
        if (n <= 0) return Array.Empty<int>();
        int a = s.WindowStart, b = s.WindowEnd;
        if (n == 1) return new[] { a };
        return Enumerable.Range(0, n).Select(i => a + (int)Math.Round((double)(b - a) * i / (n - 1))).ToList();
    }

    public static string Clock(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

    static DateTime NextAfter(IReadOnlyList<int> minutes, DateTime now)
    {
        var sorted = minutes.Order().ToList();
        foreach (var m in sorted)
        {
            var t = now.Date.AddMinutes(m);
            if (t > now.AddSeconds(30)) return t;
        }
        return now.Date.AddDays(1).AddMinutes(sorted[0]);
    }

    void Plan(string key, IReadOnlyList<int> minutes)
    {
        if (minutes.Count == 0) _platform.Cancel(key);
        else _platform.Schedule(key, NextAfter(minutes, DateTime.Now));
    }

    public Task<bool> EnsurePermissionAsync() => _platform.RequestPermissionAsync();

    /// <summary>Re-plans every reminder from the saved data. Cheap, so call it after any change and on app start.</summary>
    public async Task RefreshAllAsync()
    {
        foreach (var s in await _streaks.GetAllAsync()) Plan($"streak:{s.Id}", Slots(s));
        foreach (var s in await _streaks.GetBinAsync()) _platform.Cancel($"streak:{s.Id}");

        foreach (var key in LegacyKeys) _platform.Cancel(key);

        foreach (var c in await _categories.GetAllAsync(includeArchived: true))
        {
            if (c.ReminderOn && !c.Archived) Plan($"cat:{c.Id}", new[] { c.ReminderMinutes });
            else _platform.Cancel($"cat:{c.Id}");
        }
    }

    public void CancelStreak(int id) => _platform.Cancel($"streak:{id}");

    public void CancelCategory(int id) => _platform.Cancel($"cat:{id}");

    public async Task HandleFiredAsync(string key)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var parts = key.Split(':');
            switch (parts[0])
            {
                case "streak" when int.TryParse(parts[1], out var id):
                {
                    var s = await _streaks.GetAsync(id);
                    if (s == null || s.DeletedTicks != null || Slots(s).Count == 0) { _platform.Cancel(key); return; }
                    Plan(key, Slots(s));
                    var done = (await _streaks.GetDayChecksAsync(today)).GetValueOrDefault(id)?.Count(i => i < s.Frequency) ?? 0;
                    if (done < s.Frequency)
                        _platform.Show(key, s.Name, $"{s.Frequency - done} left today · {done}/{s.Frequency} done");
                    break;
                }
                case "cat" when int.TryParse(parts[1], out var categoryId):
                {
                    var c = await _categories.GetAsync(categoryId);
                    if (c == null || c.Archived || !c.ReminderOn) { _platform.Cancel(key); return; }
                    Plan(key, new[] { c.ReminderMinutes });
                    if ((await _expenses.GetDayAsync(c.Id, today)).Count == 0)
                        _platform.Show(key, "DayLogger", $"Anything to log in {c.Name.ToLowerInvariant()} today?");
                    break;
                }
            }
        }
        catch
        {
            // A reminder must never crash the app or the receiver; the next refresh re-plans anything missed.
        }
    }
}
