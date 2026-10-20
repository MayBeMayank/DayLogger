using System.Globalization;
using DayLogger.Controls;
using DayLogger.Data;
using DayLogger.Models;
using DayLogger.Services;

namespace DayLogger.Pages;

/// <summary>
/// Search across every entry: free words plus date range, category and spent/got-back filters. Opened from the
/// expenses section; a result opens its day.
/// </summary>
[QueryProperty(nameof(QueryText), "q")]
[QueryProperty(nameof(CategoryText), "cat")]
[QueryProperty(nameof(FromText), "from")]
[QueryProperty(nameof(ToText), "to")]
public partial class SearchPage : ContentPage
{
    const int MaxRows = 150;
    const int Custom = DateRanges.Custom;

    // Filters handed over by insights ("show me the entries behind this bar"); applied once the chips exist.
    int? _preCat;
    DateOnly? _preFrom, _preTo;
    public string QueryText { set => QueryEntry.Text = Uri.UnescapeDataString(value ?? ""); }
    public string CategoryText { set => _preCat = int.TryParse(value, out var id) ? id : null; }
    public string FromText { set => _preFrom = DateOnly.TryParseExact(value, "yyyy-MM-dd", out var d) ? d : null; }
    public string ToText { set => _preTo = DateOnly.TryParseExact(value, "yyyy-MM-dd", out var d) ? d : null; }

    readonly ExpenseRepository _expenses;
    readonly CategoryRepository _categories;
    List<ExpenseCategory> _cats = new();
    bool _ready, _first = true;
    int _runs;   // newest search wins; older ones that finish late are dropped

    public SearchPage(ExpenseRepository expenses, CategoryRepository categories)
    {
        InitializeComponent();
        _expenses = expenses;
        _categories = categories;

        WhenChips.SetItems(DateRanges.Names);
        KindChips.SetItems(new[] { "everything", "spent", "got back" });
        var today = DateTime.Today;
        FromPicker.Date = today.AddDays(-30);
        ToPicker.Date = today;
        FromPicker.SetDynamicResource(DatePicker.TextColorProperty, "Ink");
        ToPicker.SetDynamicResource(DatePicker.TextColorProperty, "Ink");
        WhenChips.SelectedIndex = 0;
        KindChips.SelectedIndex = 0;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _cats = await _categories.GetAllAsync(includeArchived: true);
        var keep = CatChips.SelectedIndex >= 0 && CatChips.SelectedIndex < _cats.Count ? _cats[CatChips.SelectedIndex].Id : (int?)null;
        _ready = false;
        CatChips.SetItems(_cats.Select(c => c.Name.ToLowerInvariant() + (c.Archived ? " (archived)" : "")));
        if (_preCat is { } pre) { keep = pre; _preCat = null; }
        if (keep is { } id) CatChips.SelectedIndex = _cats.FindIndex(c => c.Id == id);
        if (_preFrom != null || _preTo != null)
        {
            if (_preFrom is { } f) FromPicker.Date = f.ToDateTime(TimeOnly.MinValue);
            if (_preTo is { } t) ToPicker.Date = t.ToDateTime(TimeOnly.MinValue);
            _preFrom = _preTo = null;
            WhenChips.SelectedIndex = Custom;
        }
        _ready = true;

        await RunAsync();
        if (_first)
        {
            _first = false;
            QueryEntry.FocusSoon();
        }
    }

    void OnQueryChanged(object? sender, TextChangedEventArgs e) => _ = RunAsync(250);
    void OnFilterChanged(object? sender, EventArgs e)
    {
        RangeRow.IsVisible = WhenChips.SelectedIndex == Custom;
        _ = RunAsync();
    }
    void OnRangeChanged(object? sender, DateChangedEventArgs e) => _ = RunAsync(150);

    /// <summary>The range the "when" chip stands for; null ends are open.</summary>
    (DateOnly? From, DateOnly? To) Range()
    {
        return WhenChips.SelectedIndex == Custom ? OrderedPickers() : DateRanges.Preset(WhenChips.SelectedIndex);
    }

    // A reversed pair still means "between these two days".
    (DateOnly?, DateOnly?) OrderedPickers()
    {
        var a = DateOnly.FromDateTime(FromPicker.Date ?? DateTime.Today);
        var b = DateOnly.FromDateTime(ToPicker.Date ?? DateTime.Today);
        return a <= b ? (a, b) : (b, a);
    }

    async Task RunAsync(int delay = 0)
    {
        if (!_ready) return;
        var run = ++_runs;
        if (delay > 0)
        {
            await Task.Delay(delay);
            if (run != _runs) return;
        }

        var (from, to) = Range();
        var query = new ExpenseSearch
        {
            Text = QueryEntry.Text ?? "",
            From = from,
            To = to,
            CategoryId = CatChips.SelectedIndex >= 0 && CatChips.SelectedIndex < _cats.Count ? _cats[CatChips.SelectedIndex].Id : null,
            Payback = KindChips.SelectedIndex switch { 1 => false, 2 => true, _ => null },
        };
        var found = await _expenses.SearchAsync(query);
        if (run != _runs) return;
        Show(found);
    }

    void Show(List<Expense> found)
    {
        var net = found.Sum(e => e.Net);
        SummaryLabel.Text = found.Count == 0
            ? "nothing found"
            : $"{found.Count} entr{(found.Count == 1 ? "y" : "ies")} · {Money.Format(net)}" + (found.Count > MaxRows ? $" · showing the newest {MaxRows}" : "");

        Results.Clear();
        string? day = null;
        foreach (var e in found.Take(MaxRows))
        {
            if (e.Date != day)
            {
                day = e.Date;
                var date = DateOnly.ParseExact(e.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                Results.Add(Metro.Heading(date.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture)));
            }
            Results.Add(Row(e));
        }
    }

    static View Row(Expense e)
    {
        e.ShowCategory = true;
        var text = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        text.Add(new Label { Text = string.IsNullOrWhiteSpace(e.Title) ? (e.IsPayback ? "got back" : "entry") : e.Title, Style = (Style)Application.Current!.Resources["ListPrimary"] });
        if (e.HasDetail) text.Add(new Label { Text = e.Detail, Style = (Style)Application.Current!.Resources["Caption"] });

        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 12, Padding = new Thickness(20, 8) };
        row.Add(text);
        var amount = new Label { Text = e.AmountText, VerticalOptions = LayoutOptions.Center, FontAttributes = FontAttributes.Bold };
        if (e.IsPayback) amount.SetDynamicResource(Label.TextColorProperty, "Primary");
        row.Add(amount, 1);

        var date = e.Date;
        row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await Shell.Current.GoToAsync($"expenseday?date={date}")) });
        return row;
    }
}
