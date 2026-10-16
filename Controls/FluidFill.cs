using Microsoft.Maui.Devices.Sensors;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace DayLogger.Controls;

/// <summary>
/// Shared "which way is down" for every <see cref="FluidFill"/>. Reads the accelerometer (a direct gravity
/// reading, unlike a gyroscope which only reports rotation speed and drifts when integrated), converts it to
/// screen space and runs it through an under-damped spring so the liquid overshoots and sloshes.
/// A single timer drives all fills; it only runs while at least one is on screen.
/// </summary>
static class FluidMotion
{
    static readonly HashSet<FluidFill> Fills = new();
    static readonly Stopwatch Clock = new();
    static IDispatcherTimer? _timer;

    // Latest raw reading (written on the sensor thread, read on the UI thread).
    static volatile float _ax, _ay;
    static volatile bool _hasReading;

    static double _px, _py = 1, _vx, _vy; // smoothed gravity direction in screen space and its velocity

    /// <summary>Unit vector pointing to screen-down in the liquid's frame.</summary>
    public static (double X, double Y) Gravity => (_px, _py);
    /// <summary>How hard the liquid is moving: drives wave height.</summary>
    public static double Slosh { get; private set; }
    public static double Phase { get; private set; }

    public static void Attach(FluidFill fill)
    {
        Fills.Add(fill);
        if (Fills.Count != 1) return;

        try
        {
            if (Accelerometer.Default.IsSupported && !Accelerometer.Default.IsMonitoring)
            {
                Accelerometer.Default.ReadingChanged += OnReading;
                Accelerometer.Default.Start(SensorSpeed.Game);
            }
        }
        catch { /* no sensor or permission: the liquid just sits level */ }

        _timer ??= fill.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        Clock.Restart();
        _timer.Start();
    }

    public static void Detach(FluidFill fill)
    {
        Fills.Remove(fill);
        if (Fills.Count != 0) return;

        _timer?.Stop();
        try
        {
            if (Accelerometer.Default.IsMonitoring) Accelerometer.Default.Stop();
            Accelerometer.Default.ReadingChanged -= OnReading;
        }
        catch { }
        _hasReading = false;
    }

    static void OnReading(object? sender, AccelerometerChangedEventArgs e)
    {
        _ax = e.Reading.Acceleration.X;
        _ay = e.Reading.Acceleration.Y;
        _hasReading = true;
    }

    static void OnTick(object? sender, EventArgs e)
    {
        double dt = Math.Min(Clock.Elapsed.TotalSeconds, 0.05);
        Clock.Restart();

        // Target gravity direction in screen space (x right, y down).
        double tx = 0, ty = 1;
        if (_hasReading)
        {
            // The reading is the reaction to gravity (it points UP when the device rests), in device axes
            // (x right, y toward the natural top). Gravity itself is its negation; rotate that into the
            // current screen orientation (screen x right, y down).
            double ax = _ax, ay = _ay, gx, gy;
            switch (DeviceDisplay.Current.MainDisplayInfo.Rotation)
            {
                case DisplayRotation.Rotation90: gx = ay; gy = ax; break;
                case DisplayRotation.Rotation180: gx = ax; gy = -ay; break;
                case DisplayRotation.Rotation270: gx = -ay; gy = -ax; break;
                default: gx = -ax; gy = ay; break;
            }
            // Flat on a table the in-plane component is noise: ease back to "down" instead of jittering.
            double len = Math.Sqrt(gx * gx + gy * gy);
            if (len > 1e-4)
            {
                double strength = Math.Clamp(len / 0.35, 0, 1);
                tx = gx / len * strength;
                ty = gy / len * strength + (1 - strength);
                double n = Math.Sqrt(tx * tx + ty * ty);
                tx /= n; ty /= n;
            }
        }

        // Under-damped spring: overshoots, then settles.
        _vx += ((tx - _px) * 70 - _vx * 6) * dt;
        _vy += ((ty - _py) * 70 - _vy * 6) * dt;
        _px += _vx * dt;
        _py += _vy * dt;
        double m = Math.Sqrt(_px * _px + _py * _py);
        _px /= m; _py /= m;

        Slosh = Math.Sqrt(_vx * _vx + _vy * _vy);
        Phase += dt * 3;

        foreach (var f in Fills) f.Invalidate();
    }
}

/// <summary>
/// A 0..1 liquid level that keeps its surface level with the real world as the device tilts.
/// Fills the whole view; the parent clips it (e.g. a rounded Border).
/// </summary>
public class FluidFill : GraphicsView, IDrawable
{
    public static readonly BindableProperty LevelProperty =
        BindableProperty.Create(nameof(Level), typeof(double), typeof(FluidFill), 0.0, propertyChanged: (b, _, _) => ((FluidFill)b).Invalidate());

    public static readonly BindableProperty IsFullProperty =
        BindableProperty.Create(nameof(IsFull), typeof(bool), typeof(FluidFill), false, propertyChanged: (b, _, _) => ((FluidFill)b).Invalidate());

    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    /// <summary>Uses the "completed" colour.</summary>
    public bool IsFull { get => (bool)GetValue(IsFullProperty); set => SetValue(IsFullProperty, value); }

    public FluidFill()
    {
        Drawable = this;
        InputTransparent = true;
        Loaded += (_, _) => FluidMotion.Attach(this);
        Unloaded += (_, _) => FluidMotion.Detach(this);
    }

    public void Draw(ICanvas canvas, RectF r)
    {
        double level = Math.Clamp(Level, 0, 1);
        if (level <= 0 || r.Width <= 0 || r.Height <= 0) return;

        var res = Application.Current!.Resources;
        var color = res.TryGetValue(IsFull ? "PrimaryFillFull" : "PrimaryFill", out var c) && c is Color col ? col : Colors.Gray;
        canvas.FillColor = color;
        canvas.ClipRectangle(0, 0, r.Width, r.Height);

        if (level >= 0.999)
        {
            canvas.FillRectangle(0, 0, r.Width, r.Height);
            return;
        }

        double w = r.Width, h = r.Height;
        var (gx, gy) = FluidMotion.Gravity;

        // Find the plane {p : p·g = d} that leaves exactly `level` of the rectangle on gravity's side,
        // so the volume stays constant however far it tilts.
        double lo = Math.Min(Math.Min(0, w * gx), Math.Min(h * gy, w * gx + h * gy));
        double hi = Math.Max(Math.Max(0, w * gx), Math.Max(h * gy, w * gx + h * gy));
        double target = level * w * h;
        for (int i = 0; i < 22; i++)
        {
            double mid = (lo + hi) / 2;
            if (AreaBeyond(w, h, gx, gy, mid) > target) lo = mid; else hi = mid;
        }
        double d = (lo + hi) / 2;

        // Surface: a wavy line along the plane, liquid on the gravity side.
        double cx = d * gx, cy = d * gy;
        double tx = -gy, ty = gx;
        double span = Math.Sqrt(w * w + h * h);
        double amp = 0.9 + Math.Min(FluidMotion.Slosh * 3.5, 3.5);
        double k = 2 * Math.PI / Math.Max(w * 0.9, 1);

        var path = new PathF();
        for (double s = -span; s <= span; s += 3)
        {
            double off = amp * Math.Sin(k * s + FluidMotion.Phase);
            float px = (float)(cx + tx * s + gx * off), py = (float)(cy + ty * s + gy * off);
            if (s == -span) path.MoveTo(px, py); else path.LineTo(px, py);
        }
        path.LineTo((float)(cx + tx * span + gx * span * 2), (float)(cy + ty * span + gy * span * 2));
        path.LineTo((float)(cx - tx * span + gx * span * 2), (float)(cy - ty * span + gy * span * 2));
        path.Close();
        canvas.FillPath(path);
    }

    // Area of the w×h rectangle where p·g >= d (Sutherland–Hodgman clip + shoelace).
    static double AreaBeyond(double w, double h, double gx, double gy, double d)
    {
        Span<(double X, double Y)> src = stackalloc (double, double)[] { (0, 0), (w, 0), (w, h), (0, h) };
        Span<(double X, double Y)> dst = stackalloc (double, double)[8];
        int n = 0;
        for (int i = 0; i < 4; i++)
        {
            var a = src[i];
            var b = src[(i + 1) % 4];
            double da = a.X * gx + a.Y * gy - d, db = b.X * gx + b.Y * gy - d;
            if (da >= 0) dst[n++] = a;
            if ((da >= 0) != (db >= 0))
            {
                double t = da / (da - db);
                dst[n++] = (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            }
        }
        double area = 0;
        for (int i = 0; i < n; i++)
        {
            var a = dst[i];
            var b = dst[(i + 1) % n];
            area += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(area) / 2;
    }
}
