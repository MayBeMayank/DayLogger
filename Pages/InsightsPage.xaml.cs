using System.Globalization;
using DayLogger.Controls;
using DayLogger.Data;
using DayLogger.Models;
using DayLogger.Services;

namespace DayLogger.Pages;

/// <summary>
/// Where the money went over a date range, as flat bars grouped by category, subcategory, tag, person or month.
/// A bar opens the search page filtered to the entries behind it.
/// </summary>
public partial class InsightsPage : ContentPage
{
    const int MaxBars = 12;
    const int Custom = DateRanges.Custom;
    static readonly Dimension[] Dimensions = { Dimension.Category, Dimension.Subcategory, Dimension.Tag, Dimension.Person, Dimension.Month };

    readonly ExpenseRepository _expenses;
    bool _ready;
    int _runs;

    public InsightsPage(ExpenseRepository expenses)
    {
        InitializeComponent();
        _expenses = expenses;

        WhenChips.SetItems(DateRanges.Names);
        ByChips.SetItems(new[] { "category", "subcategory", "tag", "person", "month" });
        var today = DateTime.Today;
        FromPicker.Date = new DateTime(today.Year, today.Month, 1);
        ToPicker.Date = today;
        FromPicker.SetDynamicResource(DatePicker.TextColorProperty, "Ink");
        ToPicker.SetDynamicResource(DatePicker.TextColorProperty, "Ink");
        TotalLabel.FontFamily = Metro.LightFont;
        WhenChips.SelectedIndex = 1; // this month
        ByChips.SelectedIndex = 0;
        _ready = true;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RunAsync();
        await Scaffold.EnterAsync();
    }

    void OnFilterChanged(object? sender, EventArgs e)
    {
        RangeRow.IsVisible = WhenChips.SelectedIndex == Custom;
        _ = RunAsync();
    }

    void OnRangeChanged(object? sender, DateChangedEventArgs e) => _ = RunAsync(150);

    (DateOnly? From, DateOnly? To) Range()
    {
        if (WhenChips.SelectedIndex != Custom) return DateRanges.Preset(WhenChips.SelectedIndex);
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
        var entries = await _expenses.SearchAsync(new ExpenseSearch { From = from, To = to });
        if (run != _runs) return;

        var by = Dimensions[Math.Max(0, ByChips.SelectedIndex)];
        Show(entries, Breakdown.Of(entries, by), by, from, to);
    }

    void Show(List<Expense> entries, List<Slice> slices, Dimension by, DateOnly? from, DateOnly? to)
    {
        var total = entries.Sum(e => e.Net);
        TotalLabel.Text = Money.Format(total);

        // "Any time" runs from the first entry to today, so the daily average is not diluted by years before the app was used.
        var first = from ?? (entries.Count > 0 ? entries.Min(e => DateOnly.ParseExact(e.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture)) : DateOnly.FromDateTime(DateTime.Today));
        var last = to ?? DateOnly.FromDateTime(DateTime.Today);
        var days = Math.Max(1, last.DayNumber - first.DayNumber + 1);
        SummaryLabel.Text = entries.Count == 0
            ? "no entries in this range"
            : $"{entries.Count} entr{(entries.Count == 1 ? "y" : "ies")} · {Money.Format(total / days)} a day";

        Bars.Clear();
        if (entries.Count == 0) return;

        // Months stay chronological; the rest are ranked, with the long tail folded into one line.
        var shown = slices;
        if (by != Dimension.Month && slices.Count > MaxBars)
        {
            var rest = slices.Skip(MaxBars - 1).ToList();
            shown = slices.Take(MaxBars - 1).Append(new Slice($"{rest.Count} more", rest.Sum(s => s.Value), rest.Sum(s => s.Count))).ToList();
        }

        var max = shown.Max(s => s.Value);
        foreach (var s in shown) Bars.Add(Bar(s, max, total, by));
    }

    View Bar(Slice s, double max, double total, Dimension by)
    {
        var head = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 10 };
        head.Add(new Label { Text = s.Label.ToLowerInvariant(), FontSize = 16, LineBreakMode = LineBreakMode.TailTruncation });
        head.Add(new Label { Text = Money.Format(s.Value), FontAttributes = FontAttributes.Bold }, 1);

        // Fill and rest share the track as star columns, so the bar needs no drawing code and resizes with the page.
        var frac = max > 0 ? Math.Clamp(s.Value / max, 0, 1) : 0;
        var track = new Grid { HeightRequest = 10, ColumnDefinitions = { new ColumnDefinition(new GridLength(Math.Max(frac, 0.0001), GridUnitType.Star)), new ColumnDefinition(new GridLength(Math.Max(1 - frac, 0.0001), GridUnitType.Star)) } };
        var rail = new BoxView { HeightRequest = 10 };
        rail.SetDynamicResource(BoxView.ColorProperty, "Faint");
        var fill = new BoxView { HeightRequest = 10 };
        fill.SetDynamicResource(BoxView.ColorProperty, "Primary");
        track.Add(rail);
        track.SetColumnSpan(rail, 2);
        track.Add(fill);

        // Tag bars overlap (an entry sits under each of its tags), so a share of the total would mislead there.
        var note = $"{s.Count} entr{(s.Count == 1 ? "y" : "ies")}" + (by != Dimension.Tag && total > 0 && s.Value > 0 ? $" · {s.Value / total:P0}" : "");
        var stack = new VerticalStackLayout { Spacing = 4 };
        stack.Add(head);
        stack.Add(track);
        stack.Add(new Label { Text = note, Style = (Style)Application.Current!.Resources["Caption"] });

        // The "N more" line has nothing to drill into.
        if (s.Count > 0 && (s.CategoryId != null || s.Text != "" || s.From != null))
            stack.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await Shell.Current.GoToAsync(Route(s))) });
        return stack;
    }

    string Route(Slice s)
    {
        var parts = new List<string>();
        if (s.Text != "") parts.Add("q=" + Uri.EscapeDataString(s.Text));
        if (s.CategoryId is { } cat) parts.Add("cat=" + cat);
        // A month bar carries its own month; the others keep the range being analysed.
        var (from, to) = s.From != null ? (s.From, s.To) : Range();
        if (from is { } f) parts.Add("from=" + DateKey.Of(f));
        if (to is { } t) parts.Add("to=" + DateKey.Of(t));
        return "search?" + string.Join("&", parts);
    }
}
