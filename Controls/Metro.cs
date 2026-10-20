namespace DayLogger.Controls;

/// <summary>Small helpers for the Metro look (see DESIGN.md): light display type, flat list items.</summary>
public static class Metro
{
    /// <summary>Light display face for pivot headers and page titles.</summary>
    // Chosen in code rather than read from the FontLight OnPlatform resource: that resource's runtime type varies with
    // the XAML inflator and the conversion threw "cast is not valid". Keep in sync with FontLight in Styles.xaml.
    public static string LightFont =>
        DeviceInfo.Platform == DevicePlatform.Android ? "sans-serif-light" :
        DeviceInfo.Platform == DevicePlatform.WinUI ? "Segoe UI Light" :
        "HelveticaNeue-Light";

    public static Color Colour(string key) =>
        Application.Current!.Resources.TryGetValue(key, out var v) && v is Color c ? c : Colors.Gray;

    public static Color Mix(Color a, Color b, double t) => new(
        (float)(a.Red + (b.Red - a.Red) * t),
        (float)(a.Green + (b.Green - a.Green) * t),
        (float)(a.Blue + (b.Blue - a.Blue) * t));

    static Style Style(string key) => (Style)Application.Current!.Resources[key];

    /// <summary>Big light line over a muted line, no box. Tapping it runs <paramref name="tapped"/>; press-and-hold runs <paramref name="longPress"/>.</summary>
    public static View ListItem(string primary, string? secondary, Func<Task> tapped, Action? longPress = null)
    {
        var stack = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(20, 10) };
        stack.Add(new Label { Text = primary, Style = Style("ListPrimary") });
        if (!string.IsNullOrEmpty(secondary)) stack.Add(new Label { Text = secondary, Style = Style("Caption") });
        if (longPress == null)
        {
            stack.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await tapped()) });
            return stack;
        }

        // Tap and long-press both come from one behavior; see LongPressBehavior.HandleTap.
        var press = new LongPressBehavior { HandleTap = true };
        press.Tapped += async (_, _) => await tapped();
        press.Pressed += (_, _) => longPress();
        stack.Behaviors.Add(press);
        return stack;
    }

    /// <summary>
    /// A flat square live tile: a big figure top-left, the label bottom-left, solid <c>Primary</c>. Tiles are the app's own
    /// features (search, insights, people); the user's categories stay plain text rows, so the two never look alike.
    /// </summary>
    public static View Tile(string label, string stat, Func<Task> tapped)
    {
        var face = new Grid { RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) }, Padding = new Thickness(10, 8) };
        var figure = new Label { Text = stat, FontSize = 22, FontFamily = LightFont, LineBreakMode = LineBreakMode.TailTruncation };
        var name = new Label { Text = label, FontSize = 14, LineBreakMode = LineBreakMode.TailTruncation };
        figure.SetDynamicResource(Label.TextColorProperty, "OnPrimary");
        name.SetDynamicResource(Label.TextColorProperty, "OnPrimary");
        face.Add(figure);
        face.Add(name, 0, 1);

        var tile = new Border { Content = face, HeightRequest = 96, StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.Rectangle() };
        tile.SetDynamicResource(VisualElement.BackgroundColorProperty, "Primary");
        SemanticProperties.SetDescription(tile, label);
        tile.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await tapped()) });
        return tile;
    }

    /// <summary>Small uppercase muted heading that introduces a group of list items.</summary>
    public static Label Heading(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        Style = Style("SectionLabel"),
        Margin = new Thickness(20, 18, 20, 2),
    };

    public static Label Note(string text) => new() { Text = text, Style = Style("Caption"), Margin = new Thickness(20, 4) };
}
