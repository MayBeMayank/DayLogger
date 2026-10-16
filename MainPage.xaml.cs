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
            SizeChanged += (_, _) => AppTitle.Margin = new Thickness(20, Math.Clamp(Height * 0.03, 16, 32), 20, 0);
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (!_built)
            {
                _built = true;
                Pivot.SetSections(new[]
                {
                    Section(Streaks, "Streaks", add: () => Shell.Current.GoToAsync("streaks")),
                    Section(Expenses, "Expenses", add: () => Shell.Current.GoToAsync("category?id=0")),
                    Section(Settings, "Settings"),
                }, selectKey: null);
            }
            await RefreshBodiesAsync();
        }

        void OnSelectedChanged(object? sender, EventArgs e)
        {
            AddButton.IsVisible = Pivot.Selected?.Add != null;
            var key = Pivot.Selected?.Key;
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

        async Task RefreshBodiesAsync()
        {
            await FillStreaksAsync(_bodies[Streaks]);
            await FillExpensesAsync(_bodies[Expenses]);
            FillSettings(_bodies[Settings]);
        }

        // ---- streaks ----

        async Task FillStreaksAsync(VerticalStackLayout body)
        {
            var streaks = await _streaks.GetAllAsync();
            var today = DateOnly.FromDateTime(DateTime.Today);
            var checks = await _streaks.GetDayChecksAsync(today);
            int DoneToday(Streak s) => checks.GetValueOrDefault(s.Id)?.Count(i => i < s.Frequency) ?? 0;
            int left = streaks.Sum(s => Math.Max(0, s.Frequency - DoneToday(s)));

            body.Clear();
            body.Add(Big(streaks.Count == 0 ? "none yet" : left == 0 ? "all done" : $"{left} left"));
            body.Add(Metro.Note(streaks.Count == 0 ? "tap + to start a daily habit" : $"today · {streaks.Count} streak{(streaks.Count == 1 ? "" : "s")}"));

            foreach (var s in streaks)
            {
                var id = s.Id;
                var bell = ReminderService.Slots(s).Count is > 0 and var n ? $" · 🔔 {n}x" : "";
                var streak = s;
                body.Add(Metro.ListItem(s.Name.ToLowerInvariant(), $"today {DoneToday(s)}/{s.Frequency}{bell}",
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

            if (streaks.Count > 0) body.Add(Metro.Note("press and hold a streak for options"));
            var binned = await _streaks.BinCountAsync();
            if (binned > 0) body.Add(Metro.ListItem("bin", $"{binned} deleted streak{(binned == 1 ? "" : "s")}, restorable", () => Shell.Current.GoToAsync("streakbin")));
        }

        // ---- expenses: one row per category ----

        async Task FillExpensesAsync(VerticalStackLayout body)
        {
            var all = await _categories.GetAllAsync(includeArchived: true);
            var today = DateOnly.FromDateTime(DateTime.Today);

            var rows = new List<(ExpenseCategory Category, List<Expense> Month)>();
            foreach (var c in all) rows.Add((c, await _expenses.GetMonthAsync(c.Id, today.Year, today.Month)));

            var active = rows.Where(r => !r.Category.Archived).ToList();
            var total = active.Sum(r => r.Month.Sum(e => e.Net));
            var entries = active.Sum(r => r.Month.Count);

            body.Clear();
            body.Add(Big(active.Count == 0 ? "none yet" : Money.Format(total)));
            body.Add(Metro.Note(active.Count == 0
                ? "tap + to create a category (Meals, Travel, Bills…)"
                : $"{today.ToString("MMMM", CultureInfo.CurrentCulture).ToLowerInvariant()} · {entries} entr{(entries == 1 ? "y" : "ies")}"));

            var summaries = await _people.GetSummariesAsync();
            var owed = summaries.Sum(s => s.Open);
            body.Add(Metro.ListItem("people", summaries.Count == 0
                    ? "shops, vendors and friends you deal with"
                    : $"{summaries.Count} · {(owed > 0 ? Money.Format(owed) + " yet to come back" : "nothing waiting to come back")}",
                () => Shell.Current.GoToAsync("people")));

            foreach (var (c, month) in rows)
            {
                var id = c.Id;
                var sum = month.Sum(e => e.Net);
                var pending = c.Archived ? 0 : await _expenses.GetOpenAsync(id);
                var detail = c.Archived
                    ? "archived · tap to restore or delete"
                    : $"{(sum != 0 ? Money.Format(sum) + " · " : "")}{month.Count} entr{(month.Count == 1 ? "y" : "ies")} this month"
                      + (pending > 0 ? $" · {Money.Format(pending)} yet to come back" : "");

                var category = c;
                body.Add(Metro.ListItem(c.Name.ToLowerInvariant(), detail,
                    () => c.Archived ? Shell.Current.GoToAsync($"category?id={id}") : Shell.Current.GoToAsync($"expenses?cat={id}"),
                    longPress: () => Menu.Show(category.Name,
                        new BarAction("✎", "edit", () => Shell.Current.GoToAsync($"category?id={id}")),
                        category.Archived
                            ? new BarAction("↩", "restore", () => SetArchivedAsync(category, false))
                            : new BarAction("🗄", "archive", () => SetArchivedAsync(category, true)))));
            }
            if (active.Count > 0) body.Add(Metro.Note("press and hold a category for options"));
        }

        async Task SetArchivedAsync(ExpenseCategory category, bool archived)
        {
            await _categories.SetArchivedAsync(category, archived);
            await _reminders.RefreshAllAsync();
            await RefreshBodiesAsync();
        }

        // ---- settings ----

        void FillSettings(VerticalStackLayout body)
        {
            body.Clear();
            body.Add(Metro.ListItem("reminders", "nudges for streaks and categories", () => Shell.Current.GoToAsync("reminders")));
            body.Add(Metro.ListItem("backup & restore", "save your data to a file, bring it back anywhere", () => Shell.Current.GoToAsync("backup")));
            body.Add(Metro.ListItem("theme", ThemeService.Mode.ToString().ToLowerInvariant() + " · tap to change", () =>
            {
                ThemeService.Mode = (ThemeMode)(((int)ThemeService.Mode + 1) % 3);
                FillSettings(body);
                return Task.CompletedTask;
            }));
        }

        static Label Big(string text) => new()
        {
            Text = text,
            FontSize = 48,
            FontFamily = Metro.LightFont,
            Margin = new Thickness(20, 4, 20, 0),
        };
    }
}
