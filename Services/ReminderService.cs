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
/// Local reminders for streaks, meals and expenses. Each reminder key ("streak:5", "meal:1", "expense") holds a single
/// pending alarm for its next time; when it fires, the message is decided from today's data (nothing is shown if the
/// thing is already done) and the following alarm is scheduled.
/// </summary>
public class ReminderService
{
    readonly IReminderPlatform _platform;
    readonly StreakRepository _streaks;
    readonly MealRepository _meals;
    readonly ExpenseRepository _expenses;

    public ReminderService(IReminderPlatform platform, StreakRepository streaks, MealRepository meals, ExpenseRepository expenses)
    {
        _platform = platform;
        _streaks = streaks;
        _meals = meals;
        _expenses = expenses;
        _platform.Fired = HandleFiredAsync;
    }

    // Meal reminders cover these types, in this order.
    public static readonly MealType[] MealTypes = { MealType.Breakfast, MealType.Lunch, MealType.Dinner };
    static readonly int[] DefaultMealMinutes = { 8 * 60 + 30, 13 * 60, 20 * 60 };

    public static bool MealsOn { get => Preferences.Get("remind.meals", false); set => Preferences.Set("remind.meals", value); }
    public static int MealMinutes(MealType t) => Preferences.Get($"remind.meal.{(int)t}", DefaultMealMinutes[(int)t]);
    public static void SetMealMinutes(MealType t, int minutes) => Preferences.Set($"remind.meal.{(int)t}", minutes);

    public static bool ExpensesOn { get => Preferences.Get("remind.expenses", false); set => Preferences.Set("remind.expenses", value); }
    public static int ExpenseMinutes { get => Preferences.Get("remind.expenses.time", 21 * 60 + 30); set => Preferences.Set("remind.expenses.time", value); }

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

        foreach (var t in MealTypes)
        {
            if (MealsOn) Plan($"meal:{(int)t}", new[] { MealMinutes(t) });
            else _platform.Cancel($"meal:{(int)t}");
        }

        if (ExpensesOn) Plan("expense", new[] { ExpenseMinutes });
        else _platform.Cancel("expense");
    }

    public void CancelStreak(int id) => _platform.Cancel($"streak:{id}");

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
                case "meal" when int.TryParse(parts[1], out var type):
                {
                    if (!MealsOn) { _platform.Cancel(key); return; }
                    var t = (MealType)type;
                    Plan(key, new[] { MealMinutes(t) });
                    if (!(await _meals.GetDayAsync(today)).Any(m => m.Type == t))
                        _platform.Show(key, "DayLogger", $"Log your {t.ToString().ToLowerInvariant()}?");
                    break;
                }
                case "expense":
                {
                    if (!ExpensesOn) { _platform.Cancel(key); return; }
                    Plan(key, new[] { ExpenseMinutes });
                    if ((await _expenses.GetDayAsync(today)).Count == 0)
                        _platform.Show(key, "DayLogger", "Anything to log in expenses today?");
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
