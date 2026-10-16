using System.ComponentModel;
using DayLogger.Data;
using SQLite;

namespace DayLogger.Models;

public class Expense : INotifyPropertyChanged
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>Day of the expense as yyyy-MM-dd.</summary>
    [Indexed]
    public string Date { get; set; } = "";

    [Indexed]
    public int CategoryId { get; set; }

    public int? SubcategoryId { get; set; }

    public string Title { get; set; } = "";

    /// <summary>0 means "no amount" (allowed when the category doesn't require one).</summary>
    public double Amount { get; set; }
    public string? Description { get; set; }

    /// <summary>File name inside the receipts folder (not a full path, which changes between installs).</summary>
    public string? Receipt { get; set; }

    public long CreatedTicks { get; set; }

    /// <summary>Filled in by the repository when entries are loaded.</summary>
    [Ignore] public List<string> Tags { get; set; } = new();
    [Ignore] public string? SubcategoryName { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    bool _expanded, _everExpanded;

    /// <summary>UI-only: whether the list row shows its details and actions.</summary>
    [Ignore]
    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value) return;
            _expanded = value;
            if (value && !_everExpanded) { _everExpanded = true; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReceiptImage))); }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowDescription)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowReceipt)));
        }
    }

    [Ignore] public bool ShowDescription => IsExpanded && HasDescription;
    [Ignore] public bool ShowReceipt => IsExpanded && HasReceipt;
    [Ignore] public ImageSource? ReceiptImage => HasReceipt && _everExpanded ? ImageSource.FromFile(ExpenseRepository.ReceiptPath(Receipt!)) : null;

    [Ignore] public string AmountText => Amount > 0 ? Money.Format(Amount) : "";

    /// <summary>"breakfast · #home #quick", whichever parts exist.</summary>
    [Ignore]
    public string Detail => string.Join(" · ", new[] { SubcategoryName, Tags.Count > 0 ? string.Join(" ", Tags.Select(t => "#" + t)) : null }
        .Where(s => !string.IsNullOrEmpty(s)));

    [Ignore] public bool HasDetail => Detail.Length > 0;
    [Ignore] public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    [Ignore] public bool HasReceipt => !string.IsNullOrEmpty(Receipt);
    [Ignore] public string ReceiptMark => HasReceipt ? "📎" : "";
}
