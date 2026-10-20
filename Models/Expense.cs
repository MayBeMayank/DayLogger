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

    /// <summary>Who it was with (shop, vendor, a friend who borrowed): optional, across all categories.</summary>
    [Indexed]
    public int? PersonId { get; set; }

    public string Title { get; set; } = "";

    /// <summary>0 means "no amount" (allowed when the category doesn't require one).</summary>
    public double Amount { get; set; }
    public string? Description { get; set; }

    /// <summary>File name inside the receipts folder (not a full path, which changes between installs).</summary>
    public string? Receipt { get; set; }

    public long CreatedTicks { get; set; }

    /// <summary>Money coming back (refund, cashback, repayment, gain). Its Amount stays positive and is subtracted from spending.</summary>
    public bool IsPayback { get; set; }

    /// <summary>For a payback: the CreatedTicks of the expense it settles, if any (ticks, not Id, so backup restore keeps the link).</summary>
    public long? SettlesTicks { get; set; }

    /// <summary>For a spend: the user expects some of it back, so what hasn't returned yet counts as open.</summary>
    public bool ExpectBack { get; set; }

    /// <summary>For a spend: the rest was given up on, which closes the open balance without inventing money.</summary>
    public bool WrittenOff { get; set; }

    /// <summary>Filled in by the repository when entries are loaded.</summary>
    [Ignore] public List<string> Tags { get; set; } = new();
    [Ignore] public string? SubcategoryName { get; set; }
    [Ignore] public string? CategoryName { get; set; }

    /// <summary>UI-only: day pages that mix categories show each entry's category in its detail line.</summary>
    [Ignore] public bool ShowCategory { get; set; }
    [Ignore] public string? PersonName { get; set; }
    [Ignore] public string? PersonPhoto { get; set; }
    [Ignore] public bool HasPerson => PersonName != null;
    [Ignore] public List<Expense> Paybacks { get; set; } = new();
    [Ignore] public string? LinkedTitle { get; set; }

    /// <summary>What counts toward spending: paybacks are negative.</summary>
    [Ignore] public double Net => IsPayback ? -Amount : Amount;

    [Ignore] public double BackTotal => Paybacks.Sum(p => p.Amount);

    /// <summary>Expected back and not yet returned (0 once settled or written off).</summary>
    [Ignore] public double Open => IsPayback || !ExpectBack || WrittenOff ? 0 : Math.Max(0, Amount - BackTotal);

    [Ignore] public double BackFraction => Amount > 0 ? Math.Min(1, BackTotal / Amount) : 0;
    [Ignore] public bool IsSpend => !IsPayback;
    [Ignore] public bool HasSettle => !IsPayback && Amount > 0 && (BackTotal > 0 || ExpectBack);
    [Ignore] public bool HasLink => IsPayback && !string.IsNullOrEmpty(LinkedTitle);
    [Ignore] public string LinkText => "\u21A9 for " + LinkedTitle;
    [Ignore] public bool HasPaybackLines => Paybacks.Count > 0;

    /// <summary>"1,500 back · 500 open", or "settled" / "written off" once nothing is open.</summary>
    [Ignore]
    public string SettleText
    {
        get
        {
            var parts = new List<string>();
            if (BackTotal > 0) parts.Add($"{Money.Format(BackTotal)} back");
            if (Open > 0) parts.Add($"{Money.Format(Open)} open");
            else if (ExpectBack && WrittenOff) parts.Add("written off");
            else if (ExpectBack) parts.Add("settled");
            return string.Join(" · ", parts);
        }
    }

    [Ignore] public string PaybackLines => string.Join("\n", Paybacks.Select(p => $"\u2212{Money.Format(p.Amount)} · {p.Date}"));

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

    [Ignore] public string AmountText => Amount > 0 ? (IsPayback ? "\u2212" : "") + Money.Format(Amount) : "";
    [Ignore] public bool Settled => HasSettle && ExpectBack && Open <= 0;

    /// <summary>"breakfast · #home #quick", whichever parts exist.</summary>
    [Ignore]
    public string Detail => string.Join(" · ", new[] { ShowCategory ? CategoryName : null, PersonName, SubcategoryName, Tags.Count > 0 ? string.Join(" ", Tags.Select(t => "#" + t)) : null }
        .Where(s => !string.IsNullOrEmpty(s)));

    [Ignore] public bool HasDetail => Detail.Length > 0;
    [Ignore] public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    [Ignore] public bool HasReceipt => !string.IsNullOrEmpty(Receipt);
    [Ignore] public string ReceiptMark => HasReceipt ? "📎" : "";
}
