using System.Globalization;
using DayLogger.Models;

namespace DayLogger.Services;

public enum Dimension { Category, Subcategory, Tag, Person, Month }

/// <summary>One bar of an analysis. <see cref="Text"/>, <see cref="CategoryId"/> and the month range say how to list its entries in search.</summary>
public record Slice(string Label, double Value, int Count, string Text = "", int? CategoryId = null, DateOnly? From = null, DateOnly? To = null);

/// <summary>Groups entries (paybacks count negative, as everywhere) by one dimension.</summary>
public static class Breakdown
{
    public const string None = "none";

    public static List<Slice> Of(List<Expense> entries, Dimension by)
    {
        var groups = new Dictionary<string, (double Value, int Count, Slice Seed)>();
        void Add(string key, Expense e, Slice seed)
        {
            groups.TryGetValue(key, out var g);
            groups[key] = (g.Value + e.Net, g.Count + 1, g.Seed ?? seed);
        }

        foreach (var e in entries)
        {
            switch (by)
            {
                case Dimension.Category:
                    Add("c" + e.CategoryId, e, new Slice(e.CategoryName ?? "?", 0, 0, CategoryId: e.CategoryId));
                    break;
                case Dimension.Subcategory:
                    var sub = e.SubcategoryName;
                    Add("s" + e.CategoryId + "/" + sub, e, new Slice((e.CategoryName ?? "?") + (sub != null ? " › " + sub : " › " + None), 0, 0, sub ?? "", e.CategoryId));
                    break;
                case Dimension.Tag:
                    // An entry counts under each of its tags, so these bars overlap by design.
                    foreach (var t in e.Tags) Add("t" + t, e, new Slice("#" + t, 0, 0, "#" + t));
                    if (e.Tags.Count == 0) Add("t", e, new Slice("no tags", 0, 0, ""));
                    break;
                case Dimension.Person:
                    Add("p" + e.PersonId, e, new Slice(e.PersonName ?? "no person", 0, 0, e.PersonName ?? ""));
                    break;
                case Dimension.Month:
                    var d = DateOnly.ParseExact(e.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    var first = new DateOnly(d.Year, d.Month, 1);
                    Add(e.Date[..7], e, new Slice(first.ToString("MMM yyyy", CultureInfo.CurrentCulture).ToLowerInvariant(), 0, 0, From: first, To: first.AddMonths(1).AddDays(-1)));
                    break;
            }
        }

        var slices = groups.Select(g => g.Value.Seed with { Value = g.Value.Value, Count = g.Value.Count });
        return (by == Dimension.Month ? slices.OrderBy(s => s.From) : slices.OrderByDescending(s => s.Value)).ToList();
    }
}
