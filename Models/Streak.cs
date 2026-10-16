using SQLite;

namespace DayLogger.Models;

/// <summary>A reusable daily-check template, e.g. "Keep Hydrated" x 12.</summary>
public class Streak
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>How many checks make a day complete.</summary>
    public int Frequency { get; set; } = 1;

    public long CreatedTicks { get; set; }

    /// <summary>Reminders per day, spread evenly across the window below. 0 = off; never more than <see cref="Frequency"/>.</summary>
    public int ReminderCount { get; set; }

    /// <summary>Window the reminders are spread across, in minutes after midnight. Rows from before reminders existed read as 0/0.</summary>
    public int ReminderStartMin { get; set; }
    public int ReminderEndMin { get; set; }

    [Ignore] public bool HasWindow => ReminderEndMin > ReminderStartMin;
    [Ignore] public int WindowStart => HasWindow ? ReminderStartMin : 8 * 60;
    [Ignore] public int WindowEnd => HasWindow ? ReminderEndMin : 21 * 60;

    /// <summary>When it was moved to the bin (UTC ticks); null while active.</summary>
    public long? DeletedTicks { get; set; }
}

/// <summary>Checks done for one streak on one day. No row means zero.</summary>
public class StreakLog
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int StreakId { get; set; }

    /// <summary>Day as yyyy-MM-dd (sorts and range-queries as text).</summary>
    [Indexed]
    public string Date { get; set; } = "";

    public int Count { get; set; }

    /// <summary>Which boxes are ticked, as comma-separated indices (boxes are independent). Null on rows from before this existed.</summary>
    public string? Checked { get; set; }

    [Ignore]
    public HashSet<int> Indices
    {
        get => string.IsNullOrEmpty(Checked)
            ? Enumerable.Range(0, Count).ToHashSet()
            : Checked.Split(',').Select(int.Parse).ToHashSet();
        set { Checked = string.Join(',', value.Order()); Count = value.Count; }
    }
}
