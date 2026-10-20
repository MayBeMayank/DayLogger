namespace DayLogger.Services;

/// <summary>The "when" presets shared by search and insights. Index 5 is "pick dates", which the page resolves itself.</summary>
public static class DateRanges
{
    public const int Custom = 5;

    public static readonly string[] Names = { "any time", "this month", "last month", "last 30 days", "this year", "pick dates" };

    /// <summary>Range of a preset (null ends are open). "Any time" and "pick dates" have none here.</summary>
    public static (DateOnly? From, DateOnly? To) Preset(int index)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var month = new DateOnly(today.Year, today.Month, 1);
        return index switch
        {
            1 => (month, today),
            2 => (month.AddMonths(-1), month.AddDays(-1)),
            3 => (today.AddDays(-29), today),
            4 => (new DateOnly(today.Year, 1, 1), today),
            _ => (null, null),
        };
    }
}
