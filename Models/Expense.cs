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

    public string Title { get; set; } = "";
    public double Amount { get; set; }
    public string? Description { get; set; }

    /// <summary>File name inside the receipts folder (not a full path, which changes between installs).</summary>
    public string? Receipt { get; set; }

    public long CreatedTicks { get; set; }

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

    [Ignore] public bool ShowDescription => IsExpanded && HasDescription;
    [Ignore] public bool ShowReceipt => IsExpanded && HasReceipt;
    [Ignore] public ImageSource? ReceiptImage => HasReceipt && _everExpanded ? ImageSource.FromFile(ExpenseRepository.ReceiptPath(Receipt!)) : null;

    [Ignore] public string AmountText => Money.Format(Amount);
    [Ignore] public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    [Ignore] public bool HasReceipt => !string.IsNullOrEmpty(Receipt);
    [Ignore] public string ReceiptMark => HasReceipt ? "📎" : "";
}
