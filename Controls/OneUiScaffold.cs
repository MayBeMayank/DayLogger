namespace DayLogger.Controls;

/// <summary>
/// One-hand-friendly page scaffold that adapts to the window.
/// Portrait (tall): a big title sits low in the top part of the screen and <see cref="Body"/> starts below it, within
/// thumb reach. Scrolling slides and shrinks that same title into the compact top bar.
/// Landscape (wide): the title takes a left pane and <see cref="Body"/> scrolls on its own in the right pane.
/// Compact (short and narrow): plain scrolling body under the normal Shell nav bar.
/// </summary>
[ContentProperty(nameof(Body))]
public class OneUiScaffold : ContentView
{
    enum LayoutMode { Compact, Portrait, Landscape }

    const double MinHeight = 500;         // dp; portrait windows at least this tall get the collapsing header
    const double HeaderFraction = 0.45;   // share of the screen the title area takes before scrolling
    const double PaneFraction = 0.4;      // landscape: share of the width the title pane takes
    const double BarHeight = 56;
    const double LargeSize = 34;
    const double BarSize = 16;
    const double Margin = 20;
    const double BackWidth = 52;          // room the back arrow takes at the bar's left edge

    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(OneUiScaffold), "", propertyChanged: (b, _, _) => ((OneUiScaffold)b).SyncText());

    public static readonly BindableProperty SubtitleProperty =
        BindableProperty.Create(nameof(Subtitle), typeof(string), typeof(OneUiScaffold), "", propertyChanged: (b, _, _) => ((OneUiScaffold)b).SyncText());

    public static readonly BindableProperty BodyProperty =
        BindableProperty.Create(nameof(Body), typeof(View), typeof(OneUiScaffold), null,
            propertyChanged: (b, _, n) => ((OneUiScaffold)b)._bodyHost.Content = (View?)n);

    public static readonly BindableProperty AsideProperty =
        BindableProperty.Create(nameof(Aside), typeof(View), typeof(OneUiScaffold), null,
            propertyChanged: (b, _, n) => { var s = (OneUiScaffold)b; s._asideHost.Content = (View?)n; if (s.Width > 0) s.ApplyLayout(); });

    public static readonly BindableProperty AsideBelowProperty =
        BindableProperty.Create(nameof(AsideBelow), typeof(bool), typeof(OneUiScaffold), false,
            propertyChanged: (b, _, _) => { var s = (OneUiScaffold)b; if (s.Width > 0) s.ApplyLayout(); });

    readonly Grid _root = new() { ColumnDefinitions = { new ColumnDefinition(0), new ColumnDefinition(GridLength.Star) } };
    readonly ScrollView _scroll = new();
    readonly ContentView _headerSpace = new();
    readonly ContentView _bodyHost = new();
    readonly ContentView _asideHost = new();
    readonly VerticalStackLayout _stack = new() { Spacing = 0 };
    readonly ScrollView _leftScroll = new() { IsVisible = false, Margin = new Thickness(0, BarHeight, 0, 0) };
    readonly VerticalStackLayout _leftStack = new() { Spacing = 0 };
    readonly ContentView _leftSpacer = new(); // room the resting title takes at the top of the left pane
    (bool split, bool below, bool has)? _arranged;
    readonly Grid _bar = new() { HeightRequest = BarHeight, Opacity = 0, InputTransparent = true, VerticalOptions = LayoutOptions.Start };
    readonly Grid _titles = new() { InputTransparent = true };
    readonly Label _bigTitle = new() { FontSize = LargeSize, FontFamily = Metro.LightFont, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start, AnchorX = 0, AnchorY = 0 };
    readonly Label _bigSubtitle = new() { Style = (Style)Application.Current!.Resources["Caption"], HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start };
    readonly Label _back = new() { Text = "←", FontSize = 20, HeightRequest = BarHeight, VerticalTextAlignment = TextAlignment.Center, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start, Padding = new Thickness(16, 0), IsVisible = false };

    LayoutMode _mode = LayoutMode.Compact;
    double _headerHeight;
    double _collapse; // scroll distance over which the header collapses
    double _titleHeight, _subtitleHeight;

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    /// <summary>Content shown beside the title (left) in landscape; stacked with <see cref="Body"/> otherwise.</summary>
    public View? Aside { get => (View?)GetValue(AsideProperty); set => SetValue(AsideProperty, value); }
    /// <summary>Portrait only: put the aside after the body instead of before it.</summary>
    public bool AsideBelow { get => (bool)GetValue(AsideBelowProperty); set => SetValue(AsideBelowProperty, value); }
    public View? Body { get => (View?)GetValue(BodyProperty); set => SetValue(BodyProperty, value); }

    public OneUiScaffold()
    {
        _scroll.Content = _stack;
        _leftScroll.Content = _leftStack;
        _scroll.Scrolled += OnScrolled;
        _leftScroll.Scrolled += OnScrolled;

        //var barLine = new BoxView { HeightRequest = 1, VerticalOptions = LayoutOptions.End };
        //barLine.SetDynamicResource(BoxView.ColorProperty, "Faint");
        //_bar.SetDynamicResource(VisualElement.BackgroundColorProperty, "Paper");
        //_bar.Add(barLine);

        _bigTitle.SetDynamicResource(Label.TextColorProperty, "Primary");
        _titles.Add(_bigTitle);
        _titles.Add(_bigSubtitle);

        _back.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await Shell.Current.GoToAsync("..")) });

        // Children share the full grid (spanning both columns); only the scroll view moves between columns.
        foreach (var v in new View[] { _scroll, _bar, _titles, _back }) _root.SetColumnSpan(v, 2);
        _root.Add(_scroll);
        _root.Add(_leftScroll);
        _root.Add(_bar);
        _root.Add(_titles);
        _root.Add(_back);
        Content = _root;

        SizeChanged += (_, _) => ApplyLayout();
        Unloaded += (_, _) => UnhookPage();

        // Hidden until the turnstile runs so the finished layout never flashes first.
        if (!Motion.Reduced) _bodyHost.Opacity = _asideHost.Opacity = _bigTitle.Opacity = 0;
        Loaded += async (_, _) =>
        {
            HookPage();
            _loaded.TrySetResult();
            // Pages that fill their list asynchronously call EnterAsync themselves; this is only the safety net.
            if (!ManualEntrance) await EnterAsync();
            else { await Task.Delay(1500); await EnterAsync(); }
        };
    }

    /// <summary>Set when the page fills its body after loading and calls <see cref="EnterAsync"/> once that is done.</summary>
    public bool ManualEntrance { get; set; }

    bool _entered, _entering, _left;
    readonly TaskCompletionSource _loaded = new();
    Page? _page;

    // Coming back to the page (the one pushed on top was popped) replays the turnstile the other way round.
    void HookPage()
    {
        UnhookPage();
        for (Element? e = Parent; e != null; e = e.Parent)
        {
            if (e is not Page page) continue;
            _page = page;
            page.Disappearing += OnPageDisappearing;
            page.Appearing += OnPageAppearing;
            break;
        }
    }

    void UnhookPage()
    {
        if (_page == null) return;
        _page.Disappearing -= OnPageDisappearing;
        _page.Appearing -= OnPageAppearing;
        _page = null;
    }

    void OnPageDisappearing(object? sender, EventArgs e) => _left = true;

    void OnPageAppearing(object? sender, EventArgs e)
    {
        if (!_left || !_entered || _entering) return;
        _left = false;
        _ = ReplayAsync();
    }

    async Task ReplayAsync()
    {
        _entering = true;
        try
        {
            // The elements hide synchronously inside TurnstileInAsync, before the page has finished sliding back.
            await Motion.TurnstileInAsync(Elements(), forward: false);
        }
        finally { _entering = false; }
    }

    List<View> Elements()
    {
        var items = new List<View> { _bigTitle };
        if (Aside != null) items.Add(Aside);
        if (Body is Layout layout) AddElements(items, layout, 0);
        else if (Body != null) items.Add(Body);
        return items;
    }

    /// <summary>Turnstile: the title, then the aside and each top-level body element, flip in one after another. Runs once per page.</summary>
    public async Task EnterAsync()
    {
        if (_entered) return;
        _entered = true;

        // Pages that fill their list in OnAppearing get here before the scaffold is on screen; flipping views that are not
        // attached yet leaves the title turned, so the flip waits until it is loaded.
        await _loaded.Task;
        var items = Elements();

        if (Motion.Reduced) { _bodyHost.Opacity = _asideHost.Opacity = 1; return; }

        // Hide the elements first, then reveal their hosts, so nothing shows between the two steps.
        _entering = true;
        try
        {
            foreach (var v in items) v.Opacity = 0;
            _bodyHost.Opacity = _asideHost.Opacity = 1;
            await Motion.TurnstileInAsync(items);
        }
        finally { _entering = false; }
    }

    void SyncText()
    {
        _bigTitle.Text = Title.ToLowerInvariant();
        _bigSubtitle.Text = Subtitle;
        _bigSubtitle.IsVisible = !string.IsNullOrEmpty(Subtitle);
        if (Width > 0) ApplyLayout();
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        ApplyLayout();
    }

    void ApplyLayout()
    {
        if (Width <= 0 || Height <= 0) return;

        _mode = Width >= 500 && Width > Height * 1.2 ? LayoutMode.Landscape
              : Height >= MinHeight ? LayoutMode.Portrait
              : LayoutMode.Compact;
        bool large = _mode != LayoutMode.Compact;

        // Our own title/back replace the Shell nav bar whenever the scaffold is not compact.
        for (Element? e = Parent; e != null; e = e.Parent)
        {
            if (e is Page page) { Shell.SetNavBarIsVisible(page, !large); break; }
        }

        bool split = _mode == LayoutMode.Landscape && Aside != null;
        Arrange(split);
        _leftScroll.IsVisible = split;
        _asideHost.IsVisible = Aside != null;
        // Portrait: the viewport starts under the bar, so content stops at the bar instead of sliding beneath it.
        _scroll.Margin = new Thickness(0, _mode == LayoutMode.Portrait ? BarHeight : 0, 0, 0);

        double pane = _mode == LayoutMode.Landscape ? Width * PaneFraction : 0;
        _root.ColumnDefinitions[0].Width = new GridLength(pane);
        _root.SetColumn(_scroll, _mode == LayoutMode.Landscape ? 1 : 0);
        _root.SetColumnSpan(_scroll, _mode == LayoutMode.Landscape ? 1 : 2);

        _titles.IsVisible = large;
        _bar.IsVisible = _mode == LayoutMode.Portrait;
        _back.IsVisible = large && Shell.Current?.Navigation.NavigationStack.Count > 1;
        _headerSpace.IsVisible = _mode == LayoutMode.Portrait;

        if (large)
        {
            double maxWidth = Math.Max((_mode == LayoutMode.Landscape ? pane : Width) - 2 * Margin, 50);
            _bigTitle.MaximumWidthRequest = _bigSubtitle.MaximumWidthRequest = maxWidth;
            _titleHeight = _bigTitle.Measure(maxWidth, double.PositiveInfinity).Height;
            _subtitleHeight = _bigSubtitle.IsVisible ? _bigSubtitle.Measure(maxWidth, double.PositiveInfinity).Height : 0;
        }

        if (_mode == LayoutMode.Portrait)
        {
            _headerHeight = Height * HeaderFraction;
            _collapse = _headerHeight - BarHeight; // the scroll viewport already starts BarHeight down
            _headerSpace.HeightRequest = _collapse;
            // Keeps the body tall enough that the header can always fully collapse, even for short content.
            _stack.MinimumHeightRequest = _collapse + Height - BarHeight;
        }
        else
        {
            double rest = _titleHeight + (_subtitleHeight > 0 ? 4 : 0) + _subtitleHeight + 16;
            _collapse = split ? rest : 0;
            _leftSpacer.HeightRequest = rest;
            // Same trick as portrait: always enough room for the title to collapse.
            _leftStack.MinimumHeightRequest = split ? Height - BarHeight + rest : 0;
            _stack.MinimumHeightRequest = 0;
        }
        UpdateCollapse(ActiveScroll.ScrollY);
    }

    // Moves the aside and the title labels to where the current mode wants them.
    void Arrange(bool split)
    {
        var key = (split, AsideBelow, Aside != null);
        if (_arranged == key) return;
        _arranged = key;

        _stack.Clear(); _leftStack.Clear(); _titles.Clear();
        _titles.Add(_bigTitle);
        _titles.Add(_bigSubtitle);
        bool aside = Aside != null && !split;
        _stack.Add(_headerSpace);
        if (aside && !AsideBelow) _stack.Add(_asideHost);
        _stack.Add(_bodyHost);
        if (aside && AsideBelow) _stack.Add(_asideHost);

        if (split)
        {
            _leftStack.Add(_leftSpacer);
            _leftStack.Add(_asideHost);
        }
    }

    // A plain vertical stack inside the body is a list wrapper, so its rows flip individually rather than as one block.
    static void AddElements(List<View> items, Layout layout, int depth)
    {
        foreach (var v in layout.Children.OfType<View>().Where(v => v.IsVisible))
        {
            if (depth == 0 && v is VerticalStackLayout { Children.Count: > 0 } stack) AddElements(items, stack, depth + 1);
            else items.Add(v);
        }
    }

    ScrollView ActiveScroll => _arranged is { split: true } ? _leftScroll : _scroll;

    void OnScrolled(object? sender, ScrolledEventArgs e)
    {
        if (sender != ActiveScroll) return;
        UpdateCollapse(e.ScrollY);
    }

    static double Lerp(double a, double b, double t) => a + (b - a) * t;

    // Positions the single title between its big resting place and its slot in the top bar.
    void UpdateCollapse(double scrollY)
    {
        if (_mode == LayoutMode.Compact) return;

        bool split = _arranged is { split: true };

        double gap = _subtitleHeight > 0 ? 4 : 0;

        if (_mode == LayoutMode.Landscape && !split)
        {
            double top = Math.Max((Height - (_titleHeight + gap + _subtitleHeight)) / 2, BarHeight);
            Place(_bigTitle, Margin, top, 1);
            Place(_bigSubtitle, Margin, top + _titleHeight + gap, 1);
            _bigSubtitle.Opacity = 1;
            _bar.Opacity = 0;
            return;
        }

        double t = _collapse > 0 ? Math.Clamp(scrollY / _collapse, 0, 1) : 0;
        double endScale = BarSize / LargeSize;
        double scale = Lerp(1, endScale, t);

        // Landscape-split: the title rests at the top of the left pane and collapses against its scroll.
        double y0 = split ? BarHeight : _headerHeight - Margin - _subtitleHeight - gap - _titleHeight;
        double y1 = (BarHeight - _titleHeight * endScale) / 2;
        double x1 = _back.IsVisible ? BackWidth : 16;
        Place(_bigTitle, Lerp(Margin, x1, t), Lerp(y0, y1, t), scale);

        // The subtitle stays with the scrolling content and fades out.
        Place(_bigSubtitle, Margin, y0 + _titleHeight + gap - scrollY, 1);
        _bigSubtitle.Opacity = Math.Clamp(1 - t * 2, 0, 1);

        _bar.Opacity = t;
    }

    static void Place(View v, double x, double y, double scale)
    {
        v.TranslationX = x;
        v.TranslationY = y;
        v.Scale = scale;
    }
}
