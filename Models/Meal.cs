using System.ComponentModel;
using SQLite;

namespace DayLogger.Models;

public enum MealType { Breakfast, Lunch, Dinner, Snack }

public enum MealSource { Cooked, Online, Outside, Other }

public class Meal : INotifyPropertyChanged
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>Day of the meal as yyyy-MM-dd (sorts and range-queries as text).</summary>
    [Indexed]
    public string Date { get; set; } = "";

    public MealType Type { get; set; }
    public MealSource Source { get; set; }
    public string Dishes { get; set; } = "";
    public double? Price { get; set; }
    public string? Note { get; set; }
    public long CreatedTicks { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    bool _showActions;

    /// <summary>UI-only: long-press reveals the card's actions.</summary>
    [Ignore]
    public bool ShowActions
    {
        get => _showActions;
        set
        {
            if (_showActions == value) return;
            _showActions = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowActions)));
        }
    }

    [Ignore] public string Heading => $"{Type} · {Source}".ToLowerInvariant();
    [Ignore] public string PriceText => Price is > 0 ? Money.Format(Price.Value) : "";
    [Ignore] public bool HasNote => !string.IsNullOrWhiteSpace(Note);
}
