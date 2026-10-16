using SQLite;

namespace DayLogger.Models;

/// <summary>A user-defined kind of expense ("Meals", "Travel", ...). Each active one is a section on the home pivot.</summary>
public class ExpenseCategory
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Position among categories (left to right on the pivot).</summary>
    public int SortOrder { get; set; }

    /// <summary>Hidden from the pivot but its entries are kept.</summary>
    public bool Archived { get; set; }

    /// <summary>When false (Meals, say) an entry may be saved without an amount.</summary>
    public bool AmountRequired { get; set; } = true;

    public bool ReminderOn { get; set; }

    /// <summary>Minutes after midnight.</summary>
    public int ReminderMinutes { get; set; } = 21 * 60 + 30;

    /// <summary>Identifies the category across devices (backup restore matches on it).</summary>
    public long CreatedTicks { get; set; }
}

public class ExpenseSubcategory
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int CategoryId { get; set; }

    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
}

/// <summary>One tag on one expense. Tags are free text, lower-cased, and shared across all categories.</summary>
public class ExpenseTag
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ExpenseId { get; set; }

    [Indexed]
    public string Tag { get; set; } = "";
}
