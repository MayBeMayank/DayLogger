namespace DayLogger.Controls;

/// <summary>
/// Raises <see cref="Pressed"/> on a long press (touch) or right-click (Windows/Mac), the platform-standard way
/// to open a card's action menu. Attach with <c>view.Behaviors.Add(new LongPressBehavior())</c> or in XAML.
/// </summary>
public class LongPressBehavior : Behavior<View>
{
    View? _view;
    Action? _unhook;

    /// <summary>Sender is the view the behavior is attached to (so its BindingContext is the card's item).</summary>
    public event EventHandler? Pressed;

    /// <summary>
    /// Raise <see cref="Tapped"/> on a plain tap. Use this instead of a TapGestureRecognizer on the same view: on Android
    /// a recognizer swallows the touch events, so the native long-press never gets to fire.
    /// </summary>
    public bool HandleTap { get; set; }

    public event EventHandler? Tapped;

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        _view = bindable;
        bindable.HandlerChanged += OnHandlerChanged;
        Hook();
    }

    protected override void OnDetachingFrom(View bindable)
    {
        bindable.HandlerChanged -= OnHandlerChanged;
        Unhook();
        _view = null;
        base.OnDetachingFrom(bindable);
    }

    void OnHandlerChanged(object? sender, EventArgs e)
    {
        Unhook();
        Hook();
    }

    void Unhook()
    {
        _unhook?.Invoke();
        _unhook = null;
    }

    void RaiseTap() => Tapped?.Invoke(_view, EventArgs.Empty);

    void Raise()
    {
        try { HapticFeedback.Default.Perform(HapticFeedbackType.LongPress); } catch { /* not supported */ }
        Pressed?.Invoke(_view, EventArgs.Empty);
    }

    void Hook()
    {
        var platform = _view?.Handler?.PlatformView;
        if (platform == null) return;

#if ANDROID
        if (platform is Android.Views.View v)
        {
            v.LongClickable = true;
            EventHandler<Android.Views.View.LongClickEventArgs> h = (_, e) => { Raise(); e.Handled = true; };
            v.LongClick += h;
            EventHandler? click = null;
            if (HandleTap)
            {
                v.Clickable = true;
                click = (_, _) => RaiseTap();
                v.Click += click;
            }
            _unhook = () => { v.LongClick -= h; if (click != null) v.Click -= click; };
        }
#elif IOS || MACCATALYST
        if (platform is UIKit.UIView v)
        {
            var g = new UIKit.UILongPressGestureRecognizer(r =>
            {
                if (r.State == UIKit.UIGestureRecognizerState.Began) Raise();
            });
            v.AddGestureRecognizer(g);
            UIKit.UITapGestureRecognizer? tap = null;
            if (HandleTap)
            {
                tap = new UIKit.UITapGestureRecognizer(_ => RaiseTap());
                v.AddGestureRecognizer(tap);
            }
            _unhook = () => { v.RemoveGestureRecognizer(g); if (tap != null) v.RemoveGestureRecognizer(tap); };
        }
#elif WINDOWS
        if (platform is Microsoft.UI.Xaml.UIElement el)
        {
            Microsoft.UI.Xaml.Input.RightTappedEventHandler right = (_, e) => { Raise(); e.Handled = true; };
            Microsoft.UI.Xaml.Input.HoldingEventHandler hold = (_, e) =>
            {
                if (e.HoldingState == Microsoft.UI.Input.HoldingState.Started) Raise();
            };
            Microsoft.UI.Xaml.Input.TappedEventHandler tap = (_, _) => RaiseTap();
            el.RightTapped += right;
            el.Holding += hold;
            if (HandleTap) el.Tapped += tap;
            _unhook = () => { el.RightTapped -= right; el.Holding -= hold; if (HandleTap) el.Tapped -= tap; };
        }
#endif
    }
}
