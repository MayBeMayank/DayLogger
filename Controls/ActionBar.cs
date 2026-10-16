namespace DayLogger.Controls;

/// <summary>One round icon button of an <see cref="ActionBar"/>: a glyph with a small lowercase label under it.</summary>
public record BarAction(string Glyph, string Label, Func<Task> Run);

/// <summary>
/// Metro app-bar style context menu: pressing and holding an item slides this up from the bottom edge with round icon
/// buttons (edit, bin, archive...). It is an overlay: lay it over the page (spanning every row) and it stays hidden
/// until <see cref="Show"/>. A tap outside it, or <see cref="Close"/>, dismisses it.
/// </summary>
public class ActionBar : Grid
{
    const double IconSize = 52;
    const uint SlideMs = 180;

    readonly Border _bar;
    readonly Label _title;
    readonly HorizontalStackLayout _row;
    bool _closing;

    public bool IsOpen => IsVisible;

    public ActionBar()
    {
        IsVisible = false;

        // Dims whatever is behind and swallows the tap that dismisses the bar.
        var scrim = new BoxView { Opacity = 0.7 };
        scrim.SetDynamicResource(BoxView.ColorProperty, "Paper");
        scrim.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await CloseAsync()) });

        _title = new Label { HorizontalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation, FontSize = 12 };
        _title.SetDynamicResource(Label.TextColorProperty, "Muted");

        _row = new HorizontalStackLayout { Spacing = 28, HorizontalOptions = LayoutOptions.Center };

        _bar = new Border
        {
            VerticalOptions = LayoutOptions.End,
            StrokeThickness = 0,
            Padding = new Thickness(16, 14, 16, 28),
            Content = new VerticalStackLayout { Spacing = 14, Children = { _title, _row } },
        };
        _bar.SetDynamicResource(Border.BackgroundColorProperty, "Surface");

        Children.Add(scrim);
        Children.Add(_bar);
    }

    public async void Show(string title, params BarAction[] actions)
    {
        _title.Text = title.ToUpperInvariant();
        _row.Clear();
        foreach (var action in actions) _row.Add(Button(action));

        _closing = false;
        _bar.TranslationY = 240;
        IsVisible = true;
        await _bar.TranslateToAsync(0, 0, SlideMs, Easing.CubicOut);
    }

    public async Task CloseAsync()
    {
        if (!IsVisible || _closing) return;
        _closing = true;
        await _bar.TranslateToAsync(0, 240, SlideMs, Easing.CubicIn);
        IsVisible = false;
        _closing = false;
    }

    View Button(BarAction action)
    {
        var glyph = new Label
        {
            Text = action.Glyph,
            FontSize = 22,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
        };
        glyph.SetDynamicResource(Label.TextColorProperty, "Ink");

        var circle = new Border
        {
            WidthRequest = IconSize,
            HeightRequest = IconSize,
            StrokeThickness = 2,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.Ellipse(),
            HorizontalOptions = LayoutOptions.Center,
            Content = glyph,
        };
        circle.SetDynamicResource(Border.StrokeProperty, "Ink");

        var caption = new Label { Text = action.Label, FontSize = 12, HorizontalTextAlignment = TextAlignment.Center };
        caption.SetDynamicResource(Label.TextColorProperty, "Ink");

        var button = new VerticalStackLayout { Spacing = 6, Children = { circle, caption } };
        SemanticProperties.SetDescription(button, action.Label);
        button.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                await CloseAsync();
                await action.Run();
            }),
        });
        return button;
    }
}
