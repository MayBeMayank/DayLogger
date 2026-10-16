namespace DayLogger.Pages;

/// <summary>
/// Brand splash shown after the system's launch screen (which is the same yellow with the same mark, so the hand-off is
/// seamless). The mark is the app icon: a month heatmap that fills in and ends on a ticked day.
/// </summary>
public class SplashPage : ContentPage
{
    static readonly Color Yellow = Color.FromArgb("#FCB512");
    static readonly Color Ink = Color.FromArgb("#1C1B17");
    static readonly double[] Levels = { .2, .3, .45, .55, .7, .85, 1, 1, 1 };

    const double Cell = 56, Gap = 14;

    readonly List<Border> _cells = new();
    readonly Label _check = new() { Text = "✓", TextColor = Yellow, FontSize = 30, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Opacity = 0, Scale = 0.3 };
    readonly Label _title = new() { Text = "DAYLOGGER", TextColor = Ink, FontSize = 22, FontAttributes = FontAttributes.Bold, CharacterSpacing = 6, HorizontalOptions = LayoutOptions.Center, Opacity = 0, TranslationY = 16 };
    readonly Label _tagline = new() { Text = "log it. tick it. keep it going.", TextColor = Ink, FontSize = 13, HorizontalOptions = LayoutOptions.Center, Opacity = 0 };
    readonly VerticalStackLayout _stack;
    bool _started;

    public SplashPage()
    {
        BackgroundColor = Yellow;
        Shell.SetNavBarIsVisible(this, false);

        var grid = new Grid { RowSpacing = Gap, ColumnSpacing = Gap, HorizontalOptions = LayoutOptions.Center };
        for (int i = 0; i < 3; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(Cell));
            grid.ColumnDefinitions.Add(new ColumnDefinition(Cell));
        }
        for (int i = 0; i < 9; i++)
        {
            var cell = new Border
            {
                BackgroundColor = Ink.WithAlpha((float)Levels[i]),
                Stroke = Colors.Transparent,
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(14) },
                Opacity = 0,
                Scale = 0.4,
            };
            if (i == 8) cell.Content = _check;
            _cells.Add(cell);
            grid.Add(cell, i % 3, i / 3);
        }

        _stack = new VerticalStackLayout
        {
            Spacing = 28,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Children = { grid, new VerticalStackLayout { Spacing = 6, Children = { _title, _tagline } } },
        };
        Content = _stack;
    }

    /// <summary>Raised when the animation has finished and the app should show its first page.</summary>
    public event EventHandler? Finished;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started) return;
        _started = true;

        try
        {
            // Cells fill in like days being completed, one after another.
            var fills = new List<Task>();
            for (int i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                fills.Add(Task.Run(async () =>
                {
                    await Task.Delay(120 + i * 60);
                    await MainThread.InvokeOnMainThreadAsync(() => Task.WhenAll(
                        cell.FadeToAsync(1, 180, Easing.CubicOut),
                        cell.ScaleToAsync(1, 320, Easing.SpringOut)));
                }));
            }
            await Task.WhenAll(fills);

            // The last day gets ticked.
            await Task.WhenAll(_check.FadeToAsync(1, 140), _check.ScaleToAsync(1, 280, Easing.SpringOut));

            await Task.WhenAll(
                _title.FadeToAsync(1, 320, Easing.CubicOut),
                _title.TranslateToAsync(0, 0, 320, Easing.CubicOut));
            await _tagline.FadeToAsync(0.7, 260);

            await Task.Delay(450);
            await _stack.FadeToAsync(0, 220);
        }
        catch
        {
            // Never let an animation problem block the app.
        }
        Finished?.Invoke(this, EventArgs.Empty);
    }
}
