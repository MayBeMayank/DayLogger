namespace DayLogger.Controls;

/// <summary>
/// A grid whose background is filled from the left to <see cref="Fraction"/> of its width, like a progress bar that
/// is the card itself. The fill is the first child, so whatever is placed in the grid draws above it.
/// </summary>
public class FillGrid : Grid
{
    public static readonly BindableProperty FractionProperty =
        BindableProperty.Create(nameof(Fraction), typeof(double), typeof(FillGrid), 0.0, propertyChanged: (b, _, _) => ((FillGrid)b).Apply());

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }

    readonly BoxView _fill = new() { HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Fill, InputTransparent = true };

    public FillGrid()
    {
        _fill.SetDynamicResource(BoxView.ColorProperty, "PrimaryFill");
        Children.Add(_fill);
        SizeChanged += (_, _) => Apply();
    }

    void Apply() => _fill.WidthRequest = Math.Max(0, Width) * Math.Clamp(Fraction, 0, 1);
}
