using DayLogger.Controls;
using DayLogger.Data;
using DayLogger.Models;
using DayLogger.Services;

namespace DayLogger.Pages;

public partial class StreaksPage : ContentPage
{
    readonly StreakRepository _repo;
    readonly ReminderService _reminders;
    Streak? _editing;

    public StreaksPage(StreakRepository repo, ReminderService reminders)
    {
        InitializeComponent();
        _repo = repo;
        _reminders = reminders;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    async Task RefreshAsync()
    {
        var streaks = await _repo.GetAllAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var checks = await _repo.GetDayChecksAsync(today);
        var monthLogs = await _repo.GetMonthLogsAsync(today.Year, today.Month);
        var binned = await _repo.BinCountAsync();
        BinButton.Text = binned > 0 ? $"BIN ({binned})" : "BIN";

        List.Children.Clear();
        if (streaks.Count == 0)
        {
            List.Children.Add(new Label { Text = "no streaks yet - create one below", Style = (Style)Application.Current!.Resources["Caption"] });
            return;
        }
        foreach (var s in streaks)
        {
            // Fully completed days this month, not counting today (today is added live as boxes are ticked).
            var todayKey = MealRepository.Key(today);
            int before = monthLogs.Count(l => l.StreakId == s.Id && l.Date != todayKey && l.Indices.Count(i => i < s.Frequency) >= s.Frequency);
            List.Children.Add(BuildCard(s, checks.GetValueOrDefault(s.Id) ?? new HashSet<int>(), today, before));
        }
    }

    View BuildCard(Streak streak, HashSet<int> done, DateOnly today, int completedBeforeToday)
    {
        var res = Application.Current!.Resources;
        var progress = new Label { Style = (Style)res["Caption"], VerticalOptions = LayoutOptions.Center };
        var month = new Label { Style = (Style)res["Caption"], VerticalOptions = LayoutOptions.Center };
        int daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        void Show(int n)
        {
            var bell = ReminderService.Slots(streak).Count is > 0 and var c ? $" · 🔔 {c}x" : "";
            progress.Text = $"today {n}/{streak.Frequency}{bell} · tap to open calendar";
            month.Text = $"this month {completedBeforeToday + (n >= streak.Frequency ? 1 : 0)}/{daysInMonth} days";
        }
        Show(done.Count(i => i < streak.Frequency));

        var name = new Label { Text = streak.Name, FontAttributes = FontAttributes.Bold, FontSize = 16 };

        var open = new VerticalStackLayout { Spacing = 2, Children = { name, progress, month } };
        open.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await Shell.Current.GoToAsync($"streak?id={streak.Id}")),
        });

        var head = new Grid { Children = { open } };

        var ticks = new TickRow();
        ticks.Set(streak.Frequency, done);
        ticks.TicksChanged += async (_, ticked) =>
        {
            Show(ticked.Count);
            await _repo.SetChecksAsync(streak.Id, today, ticked);
        };

        var actions = new CardActions();
        var reveal = new Reveal { Body = actions };
        actions.Add("Edit", () => { reveal.IsOpen = false; BeginEdit(streak); });
        actions.Add("Move to bin", async () => await MoveToBinAsync(streak));

        var card = new Border { Padding = 12, Content = new VerticalStackLayout { Spacing = 10, Children = { head, ticks, reveal } } };

        // Long-press (right-click on desktop) flips the actions open. The title area has its own tap, so it needs the behavior too.
        foreach (View v in new View[] { card, open })
        {
            var press = new LongPressBehavior();
            press.Pressed += (_, _) => Toggle(reveal);
            v.Behaviors.Add(press);
        }
        return card;
    }

    Reveal? _openActions;

    // Only one card shows its actions at a time.
    void Toggle(Reveal reveal)
    {
        if (_openActions != null && _openActions != reveal) _openActions.IsOpen = false;
        reveal.IsOpen = !reveal.IsOpen;
        _openActions = reveal.IsOpen ? reveal : null;
    }

    async Task MoveToBinAsync(Streak streak)
    {
        await _repo.MoveToBinAsync(streak);
        _reminders.CancelStreak(streak.Id);
        if (_editing?.Id == streak.Id) ResetForm();
        await RefreshAsync();
    }

    void BeginEdit(Streak streak)
    {
        _editing = streak;
        FormTitle.Text = "EDIT STREAK";
        SaveButton.Text = "UPDATE";
        CancelButton.IsVisible = true;
        NameEntry.Text = streak.Name;
        FrequencyEntry.Text = streak.Frequency.ToString();
        ReminderSlider.Maximum = Math.Max(1, streak.Frequency);
        ReminderSlider.Value = Math.Min(streak.ReminderCount, streak.Frequency);
        StartPicker.Time = TimeSpan.FromMinutes(streak.WindowStart);
        EndPicker.Time = TimeSpan.FromMinutes(streak.WindowEnd);
        NameEntry.Focus();
    }

    void ResetForm()
    {
        _editing = null;
        FormTitle.Text = "NEW STREAK";
        SaveButton.Text = "SAVE";
        CancelButton.IsVisible = false;
        NameEntry.Text = "";
        FrequencyEntry.Text = "1";
        ReminderSlider.Value = 0;
        StartPicker.Time = TimeSpan.FromHours(8);
        EndPicker.Time = TimeSpan.FromHours(21);
    }

    // The slider tops out at the number of ticks per day: more reminders than ticks makes no sense.
    void OnFrequencyChanged(object? sender, TextChangedEventArgs e)
    {
        if (!int.TryParse(e.NewTextValue, out var freq) || freq < 1) return;
        ReminderSlider.Maximum = freq;
    }

    void OnReminderCountChanged(object? sender, ValueChangedEventArgs e)
    {
        var n = (int)Math.Round(e.NewValue);
        if (Math.Abs(n - e.NewValue) > 0.001) { ReminderSlider.Value = n; return; }
        ReminderLabel.Text = n == 0 ? "REMINDERS: OFF" : $"REMINDERS: {n} A DAY, SPREAD OVER THE TIMES BELOW";
        WindowRow.IsVisible = n > 0;
    }

    async void OnSaveClicked(object? sender, EventArgs e)
    {
        var name = NameEntry.Text?.Trim();
        if (string.IsNullOrEmpty(name)) { NameEntry.Focus(); return; }
        if (!int.TryParse(FrequencyEntry.Text, out var freq) || freq < 1) { FrequencyEntry.Focus(); return; }

        var streak = _editing ?? new Streak();
        streak.Name = name;
        streak.Frequency = freq;
        streak.ReminderCount = Math.Min((int)Math.Round(ReminderSlider.Value), freq);
        streak.ReminderStartMin = (int)(StartPicker.Time ?? TimeSpan.FromHours(8)).TotalMinutes;
        streak.ReminderEndMin = (int)(EndPicker.Time ?? TimeSpan.FromHours(21)).TotalMinutes;
        if (streak.ReminderCount > 1 && streak.ReminderEndMin <= streak.ReminderStartMin)
        {
            await DisplayAlertAsync("Reminder times", "The end time has to be after the start time.", "ok");
            return;
        }
        await _repo.SaveAsync(streak);
        if (streak.ReminderCount > 0) await _reminders.EnsurePermissionAsync();
        await _reminders.RefreshAllAsync();
        ResetForm();
        await RefreshAsync();
    }

    void OnCancelClicked(object? sender, EventArgs e) => ResetForm();

    async void OnBinClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("streakbin");
}
