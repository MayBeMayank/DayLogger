using DayLogger.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml.Input;

namespace DayLogger;

/// <summary>Reports horizontal manipulations on a <see cref="SwipeHost"/>; the vertical ScrollViewers inside keep the other axis.</summary>
public class SwipeHostHandler : LayoutHandler
{
    protected override void ConnectHandler(LayoutPanel platformView)
    {
        base.ConnectHandler(platformView);
        platformView.ManipulationMode = ManipulationModes.TranslateX;
        platformView.ManipulationStarted += OnStarted;
        platformView.ManipulationDelta += OnDelta;
        platformView.ManipulationCompleted += OnCompleted;
    }

    protected override void DisconnectHandler(LayoutPanel platformView)
    {
        platformView.ManipulationStarted -= OnStarted;
        platformView.ManipulationDelta -= OnDelta;
        platformView.ManipulationCompleted -= OnCompleted;
        base.DisconnectHandler(platformView);
    }

    SwipeHost Host => (SwipeHost)VirtualView;

    void OnStarted(object sender, ManipulationStartedRoutedEventArgs e) => Host.RaiseStarted();

    void OnDelta(object sender, ManipulationDeltaRoutedEventArgs e) => Host.RaiseDelta(e.Delta.Translation.X);

    // Linear velocity is in DIPs per millisecond.
    void OnCompleted(object sender, ManipulationCompletedRoutedEventArgs e) => Host.RaiseEnded(e.Velocities.Linear.X * 1000);
}
