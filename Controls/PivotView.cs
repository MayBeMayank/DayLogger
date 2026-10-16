using DayLogger.Services;
using Microsoft.Maui.Layouts;

namespace DayLogger.Controls;

/// <summary>One page of a <see cref="PivotView"/>: a header and the body that sits under it.</summary>
public class PivotSection
{
    public required string Key { get; init; }
    public required string Header { get; init; }
    public required View Content { get; init; }

    /// <summary>What the app's "+" button does while this section is active. Null hides the button.</summary>
    public Func<Task>? Add { get; init; }

    /// <summary>Long-press on this section's header.</summary>
    public Func<Task>? LongPress { get; init; }
}

/// <summary>
/// Windows Phone style Pivot: a row of big light headers over swipeable sections that each scroll on their own.
/// It loops (swiping past the last section arrives at the first), the next header peeks in from the right, and the
/// headers travel slower than the content (parallax). All sections stay alive, so each keeps its scroll position.
/// </summary>
public class PivotView : ContentView
{
    const double HeaderHeight = 64, Gutter = 20, Gap = 28, HeaderSize = 40;
    const double InactiveScale = 0.85; // peeking headers are drawn at this fraction of the active header's size
    const double Fling = 0.15; // seconds of release velocity that carry the swipe

    readonly Grid _root = new() { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
    readonly AbsoluteLayout _headers = new() { HeightRequest = HeaderHeight, IsClippedToBounds = true };
    readonly SwipeHost _content = new() { IsClippedToBounds = true };
    readonly Label _probe;
    readonly List<Label> _pool = new();
    readonly Dictionary<Label, (int Section, int Steps)> _slots = new();
    readonly Dictionary<int, double> _widths = new();

    List<PivotSection> _sections = new();
    double _pos;      // 0 = first section exactly in place, 1 = second, ... may run past either end while looping
    double _dragFrom;
    bool _retrying;

    public event EventHandler? SelectedChanged;

    public PivotView()
    {
        _probe = NewHeader(measureOnly: true);
        _headers.Add(_probe);
        _root.Add(_headers);
        _root.Add(_content, 0, 1);
        Content = _root;

        _content.SwipeStarted += () =>
        {
            if (_sections.Count < 2) return;
            this.AbortAnimation("pivot");
            _dragFrom = _pos;
        };
        _content.SwipeDelta += dx =>
        {
            if (_sections.Count < 2 || Width <= 0) return;
            _pos -= dx / Width;
            Render();
        };
        _content.SwipeEnded += velocity =>
        {
            if (_sections.Count < 2 || Width <= 0) return;
            var start = Math.Round(_dragFrom);
            var projected = _pos - velocity / Width * Fling;
            AnimateTo(Math.Clamp(Math.Round(projected), start - 1, start + 1));
        };

        SizeChanged += (_, _) => Render();
        Loaded += (_, _) => ThemeService.Changed += Render;
        Unloaded += (_, _) => ThemeService.Changed -= Render;
    }

    public int SelectedIndex => _sections.Count == 0 ? -1 : Mod((int)Math.Round(_pos), _sections.Count);
    public PivotSection? Selected => SelectedIndex < 0 ? null : _sections[SelectedIndex];

    /// <summary>Replaces all sections, keeping the one with <paramref name="selectKey"/> selected if it still exists.</summary>
    public void SetSections(IEnumerable<PivotSection> sections, string? selectKey)
    {
        this.AbortAnimation("pivot");
        foreach (var s in _sections) _content.Remove(s.Content);
        _sections = sections.ToList();
        foreach (var s in _sections) _content.Add(s.Content);
        _widths.Clear();
        var at = _sections.FindIndex(s => s.Key == selectKey);
        _pos = Math.Max(at, 0);
        Render();
        SelectedChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Select(string key)
    {
        var at = _sections.FindIndex(s => s.Key == key);
        if (at < 0) return;
        var from = Math.Round(_pos);
        int n = _sections.Count;
        // Shortest way round the loop.
        int steps = Mod(at - Mod((int)from, n), n);
        if (steps > n / 2) steps -= n;
        AnimateTo(from + steps);
    }

    /// <summary>1 for the active header (rank 0), easing down to <see cref="InactiveScale"/> a full step away.</summary>
    static double ScaleFor(double rank) => InactiveScale + (1 - InactiveScale) * Math.Clamp(1 - Math.Abs(rank), 0, 1);

    static int Mod(int a, int n) => ((a % n) + n) % n;

    Label NewHeader(bool measureOnly = false)
    {
        var label = new Label
        {
            FontSize = HeaderSize,
            FontFamily = Metro.LightFont,
            LineBreakMode = LineBreakMode.NoWrap,
            Opacity = measureOnly ? 0 : 1,
            InputTransparent = measureOnly,
        };
        AbsoluteLayout.SetLayoutBounds(label, new Rect(0, 0, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize));
        AbsoluteLayout.SetLayoutFlags(label, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);
        if (measureOnly) return label;

        // Scaled from the bottom-left so headers of different size share a baseline and a left edge.
        label.AnchorX = 0;
        label.AnchorY = 1;
        label.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => OnHeaderTapped(label)) });
        var press = new LongPressBehavior();
        press.Pressed += async (_, _) =>
        {
            if (_slots.TryGetValue(label, out var slot) && _sections[slot.Section].LongPress is { } run) await run();
        };
        label.Behaviors.Add(press);
        return label;
    }

    void OnHeaderTapped(Label label)
    {
        if (!_slots.TryGetValue(label, out var slot) || slot.Steps == 0) return;
        AnimateTo(Math.Round(_pos) + slot.Steps);
    }

    void AnimateTo(double target)
    {
        this.AbortAnimation("pivot");
        new Animation(v => { _pos = v; Render(); }, _pos, target, Easing.CubicOut)
            .Commit(this, "pivot", 16, 260, null, (_, canceled) =>
            {
                if (canceled) return;
                _pos = Mod((int)Math.Round(_pos), _sections.Count);
                Render();
                SelectedChanged?.Invoke(this, EventArgs.Empty);
            });
    }

    double HeaderWidth(int section)
    {
        if (_widths.TryGetValue(section, out var w)) return w;
        _probe.Text = _sections[section].Header;
        w = _probe.Measure(double.PositiveInfinity, double.PositiveInfinity).Width;
        if (w > 0) _widths[section] = w; // 0 means the label isn't on screen yet; measure again next time
        return w;
    }

    void Render()
    {
        int n = _sections.Count;
        if (n == 0 || Width <= 0) return;
        RenderContent(n);
        RenderHeaders(n);
    }

    // Each section sits at its distance from the current position, wrapped so the nearest copy on the loop is used.
    void RenderContent(int n)
    {
        for (int i = 0; i < n; i++)
        {
            double d = i - _pos;
            d -= n * Math.Floor((d + n / 2.0) / n);
            var view = _sections[i].Content;
            view.TranslationX = d * Width;
            view.InputTransparent = Math.Abs(d) > 0.5;
        }
    }

    // The header strip is an endless loop of headers: the one just left of the active header waits off-screen, the next
    // ones run out to the right edge and beyond. It shifts by header widths, not screen widths, which is the parallax.
    void RenderHeaders(int n)
    {
        double floor = Math.Floor(_pos), frac = _pos - floor;
        int first = Mod((int)floor, n);

        double firstWidth = HeaderWidth(first);
        if (firstWidth <= 0)
        {
            // Not measurable yet (the probe label has no handler on the very first pass): try again right after.
            if (!_retrying)
            {
                _retrying = true;
                Dispatcher.Dispatch(() => { _retrying = false; Render(); });
            }
            return;
        }
        // The strip moves by the width the active header will have once it has shrunk to a neighbour.
        double shift = frac * (firstWidth * InactiveScale + Gap);

        var layout = new List<(int Section, double X, int Steps, double Rank)>();
        int before = Mod(first - 1, n);
        layout.Add((before, Gutter - (HeaderWidth(before) * InactiveScale + Gap) - shift, -1, -1 - frac));

        double x = Gutter - shift;
        for (int k = 0; k < 40; k++)
        {
            int section = Mod(first + k, n);
            layout.Add((section, x, k, k - frac));
            x += HeaderWidth(section) * ScaleFor(k - frac) + Gap;
            if (x > Width + Gap) break;
        }

        while (_pool.Count < layout.Count)
        {
            var label = NewHeader();
            _pool.Add(label);
            _headers.Add(label);
        }

        Color ink = Metro.Colour("Ink"), muted = Metro.Colour("Muted");
        _slots.Clear();
        for (int i = 0; i < _pool.Count; i++)
        {
            var label = _pool[i];
            label.IsVisible = i < layout.Count;
            if (i >= layout.Count) continue;

            var (section, at, steps, rank) = layout[i];
            var text = _sections[section].Header;
            if (label.Text != text) label.Text = text;
            label.TranslationX = at;
            label.Scale = ScaleFor(rank);
            label.TextColor = Metro.Mix(muted, ink, Math.Clamp(1 - Math.Abs(rank), 0, 1));
            _slots[label] = (section, steps);
        }
    }
}
