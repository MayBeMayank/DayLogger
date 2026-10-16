namespace DayLogger.Controls;

/// <summary>
/// Slim row of flat text actions that sits inside a card, under a hairline. Cards keep it hidden and reveal it
/// on long press. Add actions from code with <see cref="Add"/>, or in XAML with ActionA/ActionB + their events
/// (the event sender is this control, so its BindingContext is the card's item).
/// </summary>
public class CardActions : ContentView
{
    readonly Grid _row = new() { ColumnSpacing = 0 };
    readonly List<(string Label, Action Run)> _items = new();

    public event EventHandler? ActionAInvoked;
    public event EventHandler? ActionBInvoked;

    public static readonly BindableProperty ActionAProperty =
        BindableProperty.Create(nameof(ActionA), typeof(string), typeof(CardActions), null,
            propertyChanged: (b, _, n) => ((CardActions)b).Add((string)n, () => ((CardActions)b).ActionAInvoked?.Invoke(b, EventArgs.Empty)));

    public static readonly BindableProperty ActionBProperty =
        BindableProperty.Create(nameof(ActionB), typeof(string), typeof(CardActions), null,
            propertyChanged: (b, _, n) => ((CardActions)b).Add((string)n, () => ((CardActions)b).ActionBInvoked?.Invoke(b, EventArgs.Empty)));

    public string? ActionA { get => (string?)GetValue(ActionAProperty); set => SetValue(ActionAProperty, value); }
    public string? ActionB { get => (string?)GetValue(ActionBProperty); set => SetValue(ActionBProperty, value); }

    public CardActions()
    {
        var line = new BoxView { HeightRequest = 1 };
        line.SetDynamicResource(BoxView.ColorProperty, "Faint");
        Content = new VerticalStackLayout { Spacing = 0, Margin = new Thickness(0, 8, 0, 0), Children = { line, _row } };
    }

    public CardActions Add(string? label, Action run)
    {
        if (string.IsNullOrEmpty(label)) return this;
        _items.Add((label, run));

        var l = new Label
        {
            Text = label.ToUpperInvariant(),
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            Padding = new Thickness(8, 12, 8, 4),
        };
        l.SetDynamicResource(Label.TextColorProperty, "Primary");
        l.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(run) });

        _row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        _row.Add(l, _items.Count - 1);
        return this;
    }
}
