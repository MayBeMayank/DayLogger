using System.Globalization;

namespace DayLogger.Controls;

/// <summary>Small text shown inside a day cell (top line, bottom line), plus an optional 0..1 fill from the bottom.</summary>
public record DayBadge(string? Top, string? Bottom, double Fill = 0);

/// <summary>
/// General-purpose month calendar. Knows nothing about meals: the host supplies per-day badges
/// via <see cref="SetBadges"/> and reacts to <see cref="DaySelected"/> / <see cref="MonthChanged"/>.
/// </summary>
public class CalendarView : ContentView
{
    const double CellHeight = 58;

    readonly Grid _grid = new() { ColumnSpacing = 4, RowSpacing = 4 };
    readonly Label _title = new() { FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalOptions = LayoutOptions.Center };
    IReadOnlyDictionary<int, DayBadge> _badges = new Dictionary<int, DayBadge>();
    DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public event EventHandler<DateOnly>? DaySelected;
    public event EventHandler<DateTime>? MonthChanged;

    DateOnly? _selected;

    /// <summary>Day drawn with a heavier border (optional).</summary>
    public DateOnly? SelectedDate
    {
        get => _selected;
        set { _selected = value; Render(); }
    }

    public DayOfWeek FirstDayOfWeek { get; set; } = DayOfWeek.Monday;

    public DateTime Month
    {
        get => _month;
        set { _month = new DateTime(value.Year, value.Month, 1); Render(); }
    }

    public CalendarView()
    {
        var header = new Grid { ColumnDefinitions = { new(60), new(GridLength.Star), new(60) }, ColumnSpacing=5, RowSpacing=5, Margin = new Thickness(0, 0, 0, 8) };
        header.Add(NavButton("<", -1), 0);
        header.Add(_title, 1);
        header.Add(NavButton(">", 1), 2);

        for (int c = 0; c < 7; c++) _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        _grid.RowDefinitions.Add(new RowDefinition(28));
        for (int r = 0; r < 6; r++) _grid.RowDefinitions.Add(new RowDefinition(CellHeight));

        Content = new VerticalStackLayout { Children = { header, _grid } };
        Render();
    }

    public void SetBadges(IReadOnlyDictionary<int, DayBadge> byDayOfMonth)
    {
        _badges = byDayOfMonth;
        Render();
    }

    View NavButton(string text, int delta)
    {
        var b = new Button { Text = text, Padding = new Thickness(0, 6) };
        b.Clicked += (_, _) =>
        {
            _badges = new Dictionary<int, DayBadge>();
            Month = _month.AddMonths(delta);
            MonthChanged?.Invoke(this, _month);
        };
        return b;
    }

    void Render()
    {
        var res = Application.Current!.Resources;
        _title.Text = _month.ToString("MMMM yyyy", CultureInfo.CurrentCulture).ToUpperInvariant();
        _grid.Children.Clear();

        for (int c = 0; c < 7; c++)
        {
            var dow = (DayOfWeek)(((int)FirstDayOfWeek + c) % 7);
            var name = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(dow);
            name = name[..Math.Min(2, name.Length)].ToUpperInvariant();
            _grid.Add(new Label { Text = name, Style = (Style)res["Caption"], HorizontalTextAlignment = TextAlignment.Center, VerticalOptions = LayoutOptions.Center }, c, 0);
        }

        int offset = ((int)_month.DayOfWeek - (int)FirstDayOfWeek + 7) % 7;
        int days = DateTime.DaysInMonth(_month.Year, _month.Month);
        var today = DateOnly.FromDateTime(DateTime.Today);

        for (int day = 1; day <= days; day++)
        {
            int slot = offset + day - 1;
            var date = new DateOnly(_month.Year, _month.Month, day);
            _badges.TryGetValue(day, out var badge);
            bool isToday = date == today;

            var stack = new VerticalStackLayout { Padding = new Thickness(4, 3), Spacing = 0 };
            stack.Add(new Label { Text = day.ToString(), FontSize = 13, FontAttributes = isToday ? FontAttributes.Bold : FontAttributes.None });
            if (badge?.Top != null) stack.Add(new Label { Text = badge.Top, FontSize = 9 });
            if (badge?.Bottom != null) stack.Add(new Label { Text = badge.Bottom, FontSize = 9, Style = (Style)res["Caption"] });

            View content = stack;
            if (badge is { Fill: > 0 })
            {
                var fill = new FluidFill { Level = badge.Fill, IsFull = badge.Fill >= 1 };
                content = new Grid { Children = { fill, stack } };
            }

            bool isSelected = date == _selected;
            var cell = new Border
            {
                Content = content,
                Padding = 0,
                StrokeThickness = isSelected ? 2 : 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(10) },
            };
            cell.SetDynamicResource(Border.StrokeProperty, isSelected ? "Primary" : "Faint");
            cell.SetDynamicResource(VisualElement.BackgroundColorProperty, isToday ? "Accent" : "Paper");
            cell.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => DaySelected?.Invoke(this, date)) });
            _grid.Add(cell, slot % 7, 1 + slot / 7);
        }
    }
}
