namespace DayLogger.Controls;

/// <summary>Row of independent tick boxes: tapping one toggles only that box.</summary>
public class TickRow : ContentView
{
    readonly FlexLayout _row = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
    readonly HashSet<int> _ticked = new();
    int _total;

    public event EventHandler<IReadOnlyCollection<int>>? TicksChanged;

    public TickRow() => Content = _row;

    public void Set(int total, IEnumerable<int> ticked)
    {
        _total = Math.Max(total, 0);
        _ticked.Clear();
        foreach (var i in ticked) if (i < _total) _ticked.Add(i);
        Rebuild();
    }

    void Rebuild()
    {
        var res = Application.Current!.Resources;
        _row.Children.Clear();
        for (int i = 0; i < _total; i++)
        {
            int index = i;
            bool on = _ticked.Contains(i);
            var box = new Border
            {
                WidthRequest = 36,
                HeightRequest = 36,
                Margin = new Thickness(0, 0, 6, 6),
                Style = (Style)res["ControlBox"],
                Content = new Label
                {
                    Text = on ? "✓" : "",
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                },
            };
            box.SetDynamicResource(VisualElement.BackgroundColorProperty, on ? "Primary" : "Paper");
            ((Label)box.Content).SetDynamicResource(Label.TextColorProperty, "OnPrimary");
            box.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() =>
                {
                    if (!_ticked.Remove(index)) _ticked.Add(index);
                    Rebuild();
                    TicksChanged?.Invoke(this, _ticked.ToList());
                }),
            });
            _row.Children.Add(box);
        }
    }
}
