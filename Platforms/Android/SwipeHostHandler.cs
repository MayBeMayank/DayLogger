using Android.Content;
using Android.Views;
using DayLogger.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace DayLogger;

/// <summary>
/// Lets <see cref="SwipeHost"/> take over a touch once it turns into a horizontal drag. Without this the vertical
/// ScrollViews inside the pivot sections consume the touch and the swipe never reaches the host.
/// </summary>
public class SwipeHostHandler : LayoutHandler
{
    protected override LayoutViewGroup CreatePlatformView() =>
        new SwipeLayoutGroup(Context, (SwipeHost)VirtualView) { CrossPlatformLayout = VirtualView };

    class SwipeLayoutGroup : LayoutViewGroup
    {
        readonly SwipeHost _host;
        readonly int _slop;
        float _downX, _downY, _lastX;
        bool _dragging;
        VelocityTracker? _velocity;

        public SwipeLayoutGroup(Context context, SwipeHost host) : base(context)
        {
            _host = host;
            _slop = ViewConfiguration.Get(context)!.ScaledTouchSlop;
        }

        public override bool OnInterceptTouchEvent(MotionEvent? e)
        {
            if (e == null) return false;
            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                    _downX = _lastX = e.GetX();
                    _downY = e.GetY();
                    _dragging = false;
                    break;

                case MotionEventActions.Move when !_dragging:
                    float dx = e.GetX() - _downX, dy = e.GetY() - _downY;
                    // Mostly sideways and past the touch slop: this is a swipe, not a vertical scroll.
                    if (Math.Abs(dx) > _slop && Math.Abs(dx) > Math.Abs(dy) * 1.2f)
                    {
                        _dragging = true;
                        _lastX = e.GetX();
                        _velocity = VelocityTracker.Obtain();
                        _velocity!.AddMovement(e);
                        Parent?.RequestDisallowInterceptTouchEvent(true);
                        _host.RaiseStarted();
                        return true;
                    }
                    break;
            }
            return _dragging || base.OnInterceptTouchEvent(e);
        }

        public override bool OnTouchEvent(MotionEvent? e)
        {
            if (e == null || !_dragging) return base.OnTouchEvent(e);

            _velocity?.AddMovement(e);
            switch (e.ActionMasked)
            {
                case MotionEventActions.Move:
                    _host.RaiseDelta(Context!.FromPixels(e.GetX() - _lastX));
                    _lastX = e.GetX();
                    break;

                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    double v = 0;
                    if (e.ActionMasked == MotionEventActions.Up && _velocity != null)
                    {
                        _velocity.ComputeCurrentVelocity(1000);
                        v = Context!.FromPixels(_velocity.XVelocity);
                    }
                    _velocity?.Recycle();
                    _velocity = null;
                    _dragging = false;
                    _host.RaiseEnded(v);
                    break;
            }
            return true;
        }
    }
}
