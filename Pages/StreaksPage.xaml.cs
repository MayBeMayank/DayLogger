using DayLogger.Controls;
using DayLogger.Data;
using DayLogger.Models;
using DayLogger.Services;

namespace DayLogger.Pages;

/// <summary>Creates a streak, or edits one when opened with an id (long-press on the home list).</summary>
[QueryProperty(nameof(IdText), "id")]
public partial class StreaksPage : ContentPage
{
    readonly StreakRepository _repo;
    readonly ReminderService _reminders;
    Streak? _editing;
    int _id;
    bool _loaded;

    public string IdText { set => _id = int.TryParse(value, out var id) ? id : 0; }

    public StreaksPage(StreakRepository repo, ReminderService reminders)
    {
        InitializeComponent();
        _repo = repo;
        _reminders = reminders;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return; // coming back from a dialog, keep what's typed
        _loaded = true;

        if (_id == 0)
        {
            NameEntry.FocusSoon();
            return;
        }

        _editing = await _repo.GetAsync(_id);
        if (_editing == null) { await Shell.Current.GoToAsync(".."); return; }

        Scaffold.Title = "edit streak";
        SaveButton.Text = "UPDATE";
        BinButton.IsVisible = true;
        NameEntry.Text = _editing.Name;
        FrequencyEntry.Text = _editing.Frequency.ToString();
        ReminderSlider.Maximum = Math.Max(1, _editing.Frequency);
        ReminderSlider.Value = Math.Min(_editing.ReminderCount, _editing.Frequency);
        StartPicker.Time = TimeSpan.FromMinutes(_editing.WindowStart);
        EndPicker.Time = TimeSpan.FromMinutes(_editing.WindowEnd);
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
        await Shell.Current.GoToAsync("..");
    }

    async void OnBinClicked(object? sender, EventArgs e)
    {
        if (_editing == null) return;
        await _repo.MoveToBinAsync(_editing);
        _reminders.CancelStreak(_editing.Id);
        await Shell.Current.GoToAsync("..");
    }
}
