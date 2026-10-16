namespace DayLogger.Controls;

/// <summary>
/// Content that slides open/closed (height and fade) when <see cref="IsOpen"/> changes. Closed it keeps zero
/// height but stays in the layout, so it always knows its width and can measure its own open height.
/// </summary>
[ContentProperty(nameof(Body))]
public class Reveal : ContentView
{
    const string AnimName = "reveal";
    const uint Duration = 220;

    public static readonly BindableProperty IsOpenProperty =
        BindableProperty.Create(nameof(IsOpen), typeof(bool), typeof(Reveal), false,
            propertyChanged: (b, _, n) => ((Reveal)b).Run((bool)n));

    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }

    public View? Body { get => Content; set => Content = value; }

    public Reveal()
    {
        IsClippedToBounds = true;
        HeightRequest = 0;
        Opacity = 0;
    }

    void Run(bool open)
    {
        this.AbortAnimation(AnimName);
        double from = Height > 0 ? Height : 0;

        if (open)
        {
            double width = Width > 0 ? Width : 300;
            double target = Content?.Measure(width, double.PositiveInfinity).Height ?? 0;
            Animate(from, target, Opacity, 1, () => HeightRequest = -1); // back to auto-size once open
        }
        else
        {
            HeightRequest = from; // pin the current height so it can shrink from there
            Animate(from, 0, Opacity, 0, () => HeightRequest = 0);
        }
    }

    void Animate(double h0, double h1, double o0, double o1, Action done)
    {
        new Animation(t =>
        {
            HeightRequest = h0 + (h1 - h0) * t;
            Opacity = o0 + (o1 - o0) * t;
        }, 0, 1, Easing.CubicInOut).Commit(this, AnimName, 16, Duration, finished: (_, cancelled) =>
        {
            if (!cancelled) done();
        });
    }
}
