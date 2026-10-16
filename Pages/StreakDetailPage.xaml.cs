using System.Globalization;
using DayLogger.Controls;
using DayLogger.Data;
using DayLogger.Models;
using DayLogger.Services;

namespace DayLogger.Pages;

[QueryProperty(nameof(StreakId), "id")]
public partial class StreakDetailPage : ContentPage
{
    readonly StreakRepository _repo;
    Streak? _streak;
    Dictionary<string, HashSet<int>> _checks = new();
    StreakCard? _card;
    DateOnly _day = DateOnly.FromDateTime(DateTime.Today);

    public int StreakId { get; set; }

    public StreakDetailPage(StreakRepository repo)
    {
        InitializeComponent();
        _repo = repo;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _streak = await _repo.GetAsync(StreakId);
        if (_streak == null) return;
        Title = Scaffold.Title = _streak.Name.ToUpperInvariant();
        Scaffold.Subtitle = $"{_streak.Frequency}x per day";
        _checks = await _repo.GetChecksAsync(StreakId);
        Calendar.SelectedDate = _day;
        ShowDay();
        ShowMonth();
    }

    HashSet<int> TicksOn(DateOnly d) =>
        _checks.GetValueOrDefault(MealRepository.Key(d))?.Where(i => i < _streak!.Frequency).ToHashSet() ?? new HashSet<int>();

    int CountOn(DateOnly d) => TicksOn(d).Count;

    void ShowDay()
    {
        DayLabel.Text = _day.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture).ToUpperInvariant();
        Ticks.Set(_streak!.Frequency, TicksOn(_day));
    }

    void ShowMonth()
    {
        var m = Calendar.Month;
        int days = DateTime.DaysInMonth(m.Year, m.Month);
        var badges = new Dictionary<int, DayBadge>();
        int complete = 0;
        for (int d = 1; d <= days; d++)
        {
            int n = CountOn(new DateOnly(m.Year, m.Month, d));
            if (n == 0) continue;
            if (n >= _streak!.Frequency) complete++;
            badges[d] = new DayBadge(null, $"{n}/{_streak.Frequency}", (double)n / _streak.Frequency);
        }
        Calendar.SetBadges(badges);
        MonthLabel.Text = $"{complete} of {days} days completed";
        var (current, best) = Streaks();
        StreakLabel.Text = $"current streak {current} · best {best}";
        _card = new StreakCard(_streak!.Name, _streak.Frequency, m, badges.ToDictionary(kv => kv.Key, kv => kv.Value.Fill), complete, current, best);
    }

    async void OnShareClicked(object? sender, EventArgs e)
    {
        if (_card == null) return;
        var card = _card;
        var choice = await DisplayActionSheetAsync("Share this month", "Cancel", null, "Image card", "Text only");
        try
        {
            var caption = $"{card.Name}: {card.Current}-day streak 🔥 · {card.CompleteDays} days completed in {card.Month:MMMM yyyy} #DayLogger";
            if (choice == "Text only")
                await Share.Default.RequestAsync(new ShareTextRequest { Text = caption, Title = card.Name });
            else if (choice == "Image card")
            {
                var file = Path.Combine(FileSystem.CacheDirectory, $"streak-{card.Month:yyyyMM}-{StreakId}.png");
                await File.WriteAllBytesAsync(file, ShareCardRenderer.RenderStreak(card));
                await Share.Default.RequestAsync(new ShareFileRequest { Title = card.Name, File = new ShareFile(file) });
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Couldn't share", ex.Message, "ok");
        }
    }

    // Consecutive fully-completed days. Today not being done yet doesn't break the current streak.
    (int Current, int Best) Streaks()
    {
        var done = _checks
            .Where(kv => kv.Value.Count(i => i < _streak!.Frequency) >= _streak!.Frequency)
            .Select(kv => DateOnly.ParseExact(kv.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            .OrderBy(d => d)
            .ToList();
        var set = done.ToHashSet();

        int best = 0, run = 0;
        DateOnly? prev = null;
        foreach (var d in done)
        {
            run = prev == d.AddDays(-1) ? run + 1 : 1;
            best = Math.Max(best, run);
            prev = d;
        }

        var cursor = DateOnly.FromDateTime(DateTime.Today);
        if (!set.Contains(cursor)) cursor = cursor.AddDays(-1);
        int current = 0;
        while (set.Contains(cursor)) { current++; cursor = cursor.AddDays(-1); }
        return (current, best);
    }

    void OnDaySelected(object? sender, DateOnly day)
    {
        if (_streak == null) return;
        _day = day;
        Calendar.SelectedDate = day;
        ShowDay();
    }

    void OnMonthChanged(object? sender, DateTime month)
    {
        if (_streak != null) ShowMonth();
    }

    async void OnTicksChanged(object? sender, IReadOnlyCollection<int> ticked)
    {
        if (_streak == null) return;
        var day = _day;
        _checks[MealRepository.Key(day)] = ticked.ToHashSet();
        await _repo.SetChecksAsync(_streak.Id, day, ticked);
        ShowMonth();
    }
}
