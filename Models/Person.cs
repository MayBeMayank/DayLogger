using SQLite;

namespace DayLogger.Models;

/// <summary>
/// Someone an expense was with: a shop, a vendor, a friend who borrowed money. DayLogger keeps its own copy (name, optional
/// phone) so backups carry it and nothing depends on the phone's address book after it was picked.
/// </summary>
public class Person
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string Name { get; set; } = "";

    public string? Phone { get; set; }
    public string? Note { get; set; }

    /// <summary>Identifies the person across devices (backup restore matches on it).</summary>
    public long CreatedTicks { get; set; }
}

/// <summary>One person's totals across every category.</summary>
public record PersonSummary(Person Person, double Spent, double Back, double Open, int Entries)
{
    public static PersonSummary Of(Person person, IEnumerable<Expense> rows)
    {
        var mine = rows.Where(r => r.PersonId == person.Id).ToList();
        return new PersonSummary(person,
            mine.Where(r => !r.IsPayback).Sum(r => r.Amount),
            mine.Where(r => r.IsPayback).Sum(r => r.Amount),
            mine.Sum(r => r.Open),
            mine.Count);
    }

    /// <summary>"spent 2,000 · back 1,500 · 500 open", or a hint when there is nothing yet.</summary>
    public string Text
    {
        get
        {
            if (Entries == 0) return "no entries yet";
            var parts = new List<string>();
            if (Spent > 0) parts.Add($"spent {Money.Format(Spent)}");
            if (Back > 0) parts.Add($"back {Money.Format(Back)}");
            if (Open > 0) parts.Add($"{Money.Format(Open)} open");
            return parts.Count > 0 ? string.Join(" · ", parts) : $"{Entries} entr{(Entries == 1 ? "y" : "ies")}";
        }
    }
}
