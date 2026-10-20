using System.Globalization;
using DayLogger.Controls;
using DayLogger.Data;
using DayLogger.Models;
using DayLogger.Services;

namespace DayLogger
{
    /// <summary>
    /// Home: a Metro pivot with three sections. Streaks lists every streak, expenses lists every category, settings
    /// holds reminders, backup and the theme. "+" opens the add flow of whichever section is showing.
    /// </summary>
    public partial class MainPage : ContentPage
    {
        const string Streaks = "streaks", Expenses = "expenses", Settings = "settings";

        readonly StreakRepository _streaks;
        readonly CategoryRepository _categories;
        readonly ExpenseRepository _expenses;
        readonly PersonRepository _people;
        readonly ReminderService _reminders;

        const string SectionPref = "home.section";
        const double TopButtonAfter = 300; // dp scrolled before the back-to-top button shows

        readonly Dictionary<string, VerticalStackLayout> _bodies = new();
        readonly Dictionary<string, ScrollView> _scrolls = new();
        bool _built;

        public MainPage()
        {
            InitializeComponent();
            var services = IPlatformApplication.Current!.Services;
            _streaks = services.GetRequiredService<StreakRepository>();
            _categories = services.GetRequiredService<CategoryRepository>();
            _expenses = services.GetRequiredService<ExpenseRepository>();
            _people = services.GetRequiredService<PersonRepository>();
            _reminders = services.GetRequiredService<ReminderService>();
            QuickAdd.Requested += () => MainThread.BeginInvokeOnMainThread(OpenQuickAdd);
            SizeChanged += (_, _) => AppTitle.Margin = new Thickness(20, Math.Clamp(Height * 0.03, 16, 32), 20, 0);
            // The section about to slide in is emptied first, so its items can fly in once it has arrived.
            Pivot.Approaching += (section, side) => HideItems(section.Key, side);
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            // Items fly in on the first show only. Coming back from a pushed page the list is already on screen, and
            // hiding it after the fact (OnAppearing runs once the page is visible) flashed it, so it just refreshes in place.
            bool first = !_built;
            if (first)
            {
                _built = true;
                Pivot.SetSections(new[]
                {
                    Section(Expenses, "Expenses", add: AddExpenseAsync),
                    Section(Streaks, "Streaks", add: () => Shell.Current.GoToAsync("streaks")),
                    Section(Settings, "Settings"),
                }, selectKey: Preferences.Get(SectionPref, null));
            }
            var key = Pivot.Selected?.Key;
            if (first && key != null) _entering = (key, 1);
            if (first) OpenQuickAdd(); // a widget tap that cold-started the app
            try { await RefreshBodiesAsync(); }
            finally
            {
                _entering = null;
                if (first && key != null && key == Pivot.Selected?.Key) Play(key, 1);
            }
        }

        void OnSelectedChanged(object? sender, EventArgs e)
        {
            AddButton.IsVisible = Pivot.Selected?.Add != null;
            var key = Pivot.Selected?.Key;
            if (key != null) Preferences.Set(SectionPref, key); // reopening the app lands on the section it was left on
            if (key != null && key != _shown) { _shown = key; Play(key, Pivot.Direction); }
            TopButton.IsVisible = key != null && _scrolls.TryGetValue(key, out var scroll) && scroll.ScrollY > TopButtonAfter;
        }

        async void OnTopTapped(object? sender, TappedEventArgs e)
        {
            if (Pivot.Selected is { } section && _scrolls.TryGetValue(section.Key, out var scroll)) await scroll.ScrollToAsync(0, 0, true);
        }

        // Android's back button closes the press-and-hold menu before it leaves the page.
        protected override bool OnBackButtonPressed()
        {
            if (!Menu.IsOpen) return base.OnBackButtonPressed();
            _ = Menu.CloseAsync();
            return true;
        }

        async void OnAddTapped(object? sender, TappedEventArgs e)
        {
            if (Pivot.Selected?.Add is { } add) await add();
        }

        static string Today => DateKey.Of(DateOnly.FromDateTime(DateTime.Today));

        PivotSection Section(string key, string header, Func<Task>? add = null)
        {
            var body = new VerticalStackLayout { Spacing = 0, Padding = new Thickness(0, 8, 0, 120) };
            _bodies[key] = body;
            var scroll = new ScrollView { Content = body };
            _scrolls[key] = scroll;
            scroll.Scrolled += (_, e) =>
            {
                if (Pivot.Selected?.Key == key) TopButton.IsVisible = e.ScrollY > TopButtonAfter;
            };
            return new PivotSection { Key = key, Header = header, Content = scroll, Add = add };
        }

        void OpenQuickAdd()
        {
            if (QuickAdd.Take()) _ = AddExpenseAsync();
        }

        // "+" on expenses logs an entry (the form picks its category); with no category yet there is nothing to file under.
        async Task AddExpenseAsync() =>
            await Shell.Current.GoToAsync((await _categories.GetAllAsync()).Count == 0 ? "category?id=0" : $"expenseday?date={Today}&add=1");

        async Task RefreshBodiesAsync()
        {
            await FillStreaksAsync(_bodies[Streaks]);
            await FillExpensesAsync(_bodies[Expenses]);
            FillSettings(_bodies[Settings]);
        }

        // Items fly in every time a section arrives (from the side it came from) and when the page is shown again;
        // refreshes in place (archive, bin, theme) just swap the rows.
        string? _shown;                       // section whose items last flew in
        (string Key, int From)? _entering;    // section whose rebuilt rows must start hidden until Play

        void Swap(VerticalStackLayout body, List<View> items)
        {
            var key = _bodies.First(b => b.Value == body).Key;
            if (_entering is { } e && e.Key == key) Motion.Hide(items, e.From);
            body.Clear();
            foreach (var item in items) body.Add(item);
        }

        void HideItems(string key, int from)
        {
            if (_bodies.TryGetValue(key, out var body)) Motion.Hide(body.Children.OfType<View>().ToList(), from);
        }

        void Play(string key, int from)
        {
            if (!_bodies.TryGetValue(key, out var body) || body.Count == 0) return;
            _ = Motion.FlyInAsync(body.Children.OfType<View>().ToList(), from);
        }

        // ---- streaks ----

        async Task FillStreaksAsync(VerticalStackLayout body)
        {
            var items = new List<View>(); // swapped in at the end so the list never shows half-built
            var streaks = await _streaks.GetAllAsync();
            var today = DateOnly.FromDateTime(DateTime.Today);
            var checks = await _streaks.GetDayChecksAsync(today);
            int DoneToday(Streak s) => checks.GetValueOrDefault(s.Id)?.Count(i => i < s.Frequency) ?? 0;
            int left = streaks.Sum(s => Math.Max(0, s.Frequency - DoneToday(s)));

            items.Add(Big(Streaks, streaks.Count == 0 ? "none yet" : left == 0 ? "all done" : $"{left} left"));
            items.Add(Metro.Note(streaks.Count == 0 ? "tap + to start a daily habit" : $"today · {streaks.Count} streak{(streaks.Count == 1 ? "" : "s")}"));

            foreach (var s in streaks)
            {
                var id = s.Id;
                var bell = ReminderService.Slots(s).Count is > 0 and var n ? $" · 🔔 {n}x" : "";
                var streak = s;
                items.Add(Metro.ListItem(s.Name.ToLowerInvariant(), $"today {DoneToday(s)}/{s.Frequency}{bell}",
                    () => Shell.Current.GoToAsync($"streak?id={id}"),
                    longPress: () => Menu.Show(streak.Name,
                        new BarAction("✎", "edit", () => Shell.Current.GoToAsync($"streaks?id={id}")),
                        new BarAction("🗑", "bin", async () =>
                        {
                            await _streaks.MoveToBinAsync(streak);
                            _reminders.CancelStreak(id);
                            await RefreshBodiesAsync();
                        }))));
            }

            if (streaks.Count > 0) items.Add(Metro.Note("press and hold a streak for options"));
            var binned = await _streaks.BinCountAsync();
            if (binned > 0) items.Add(Metro.ListItem("bin", $"{binned} deleted streak{(binned == 1 ? "" : "s")}, restorable", () => Shell.Current.GoToAsync("streakbin")));
            Swap(body, items);
        }

        // ---- expenses: one row per category ----

        async Task FillExpensesAsync(VerticalStackLayout body)
        {
            var items = new List<View>(); // swapped in at the end so the list never shows half-built
            var all = await _categories.GetAllAsync(includeArchived: true);
            var today = DateOnly.FromDateTime(DateTime.Today);

            var rows = new List<(ExpenseCategory Category, List<Expense> Month)>();
            foreach (var c in all) rows.Add((c, await _expenses.GetMonthAsync(c.Id, today.Year, today.Month)));

            var active = rows.Where(r => !r.Category.Archived).ToList();
            var total = active.Sum(r => r.Month.Sum(e => e.Net));
            var entries = active.Sum(r => r.Month.Count);

            // The headline opens the all-categories calendar, whose days show everything spent that day.
            items.Add(Big(Expenses, active.Count == 0 ? "none yet" : Money.Format(total), () => Shell.Current.GoToAsync("expenses")));
            items.Add(Metro.Note(active.Count == 0
                ? "tap + to create a category (Meals, Travel, Bills…)"
                : $"{today.ToString("MMMM", CultureInfo.CurrentCulture).ToLowerInvariant()} · {entries} entr{(entries == 1 ? "y" : "ies")} · tap the total for a day-wise view"));

            // The app's own tools are solid tiles; everything below the CATEGORIES heading is the user's own text rows.
            var summaries = await _people.GetSummariesAsync();
            var tiles = new Grid { ColumnSpacing = 6, Padding = new Thickness(20, 12, 20, 0), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
            tiles.Add(Metro.Tile("search", (await _expenses.CountAsync()).ToString(), () => Shell.Current.GoToAsync("search")));
            tiles.Add(Metro.Tile("insights", Money.Compact(total), () => Shell.Current.GoToAsync("insights")), 1);
            tiles.Add(Metro.Tile("people", summaries.Count.ToString(), () => Shell.Current.GoToAsync("people")), 2);
            items.Add(tiles);
            items.Add(Metro.Heading("categories"));

            foreach (var (c, month) in rows)
            {
                var id = c.Id;
                var sum = month.Sum(e => e.Net);
                var pending = c.Archived ? 0 : await _expenses.GetOpenAsync(id);
                var detail = c.Archived
                    ? "archived · tap to restore or delete"
                    : $"{(sum != 0 ? Money.Format(sum) + " · " : "")}{month.Count} entr{(month.Count == 1 ? "y" : "ies")} this month"
                      + (pending > 0 ? $" · {Money.Format(pending)} yet to come back" : "");

                items.Add(Metro.ListItem(c.Name.ToLowerInvariant(), detail,
                    () => c.Archived ? Shell.Current.GoToAsync($"category?id={id}") : Shell.Current.GoToAsync($"expenses?cat={id}")));
            }
            Swap(body, items);
        }

        // ---- settings ----

        void FillSettings(VerticalStackLayout body)
        {
            body.Clear();
            body.Add(Metro.ListItem("manage categories", "add, edit, reorder or archive expense categories", () => Shell.Current.GoToAsync("categories")));
            body.Add(Metro.ListItem("reminders", "nudges for streaks and categories", () => Shell.Current.GoToAsync("reminders")));
            body.Add(Metro.ListItem("backup & restore", "save your data to a file, bring it back anywhere", () => Shell.Current.GoToAsync("backup")));
            body.Add(Metro.ListItem("theme", ThemeService.Mode.ToString().ToLowerInvariant() + " · tap to change", () =>
            {
                ThemeService.Mode = (ThemeMode)(((int)ThemeService.Mode + 1) % 3);
                FillSettings(body);
                return Task.CompletedTask;
            }));
            if (_entering is { } e && e.Key == Settings) Motion.Hide(body.Children.OfType<View>().ToList(), e.From);
        }

        // One headline label per section, reused across refreshes: rebuilding a 48sp label on every return flashed.
        // Wrapped in a stack like every other row (Metro.ListItem): the rows animate cleanly, a bare 48sp label did not.
        readonly Dictionary<string, (VerticalStackLayout Host, Label Label)> _big = new();

        View Big(string key, string text, Func<Task>? tapped = null)
        {
            if (!_big.TryGetValue(key, out var big))
            {
                var label = new Label { FontSize = 48, FontFamily = Metro.LightFont };
                var host = new VerticalStackLayout { Padding = new Thickness(20, 4, 20, 0), Children = { label } };
                if (tapped != null) host.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await tapped()) });
                _big[key] = big = (host, label);
            }
            if (big.Label.Text != text) big.Label.Text = text;
            return big.Host;
        }
    }
}
