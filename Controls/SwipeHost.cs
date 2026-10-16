namespace DayLogger.Controls;

/// <summary>
/// A grid that reports horizontal swipes even when its children scroll vertically (the pivot's sections do).
/// Android and Windows hook the platform touch pipeline in SwipeHostHandler so a horizontal drag wins over the child's
/// vertical scroll; elsewhere a plain pan gesture is the best effort.
/// </summary>
public class SwipeHost : Grid
{
    /// <summary>A horizontal drag began.</summary>
    public event Action? SwipeStarted;

    /// <summary>Distance moved since the last call, in device-independent units (positive = right).</summary>
    public event Action<double>? SwipeDelta;

    /// <summary>The finger lifted; velocity in device-independent units per second (positive = right).</summary>
    public event Action<double>? SwipeEnded;

    public void RaiseStarted() => SwipeStarted?.Invoke();
    public void RaiseDelta(double dx) => SwipeDelta?.Invoke(dx);
    public void RaiseEnded(double velocity) => SwipeEnded?.Invoke(velocity);

#if !ANDROID && !WINDOWS
    double _last;

    public SwipeHost()
    {
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _last = 0;
                    RaiseStarted();
                    break;
                case GestureStatus.Running:
                    RaiseDelta(e.TotalX - _last);
                    _last = e.TotalX;
                    break;
                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    RaiseEnded(0);
                    break;
            }
        };
        GestureRecognizers.Add(pan);
    }
#endif
}
