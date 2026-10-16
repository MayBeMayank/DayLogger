using DayLogger.Models;
using DayLogger.Services;

namespace DayLogger.Pages;

public partial class RemindersPage : ContentPage
{
    readonly ReminderService _reminders;
    bool _loading;

    public RemindersPage(ReminderService reminders)
    {
        InitializeComponent();
        _reminders = reminders;
    }

    TimePicker PickerFor(MealType t) => t switch
    {
        MealType.Breakfast => BreakfastPicker,
        MealType.Lunch => LunchPicker,
        _ => DinnerPicker,
    };

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _loading = true;
        MealsSwitch.IsToggled = ReminderService.MealsOn;
        foreach (var t in ReminderService.MealTypes) PickerFor(t).Time = TimeSpan.FromMinutes(ReminderService.MealMinutes(t));
        ExpensesSwitch.IsToggled = ReminderService.ExpensesOn;
        ExpensesPicker.Time = TimeSpan.FromMinutes(ReminderService.ExpenseMinutes);
        ShowEnabled();
        _loading = false;
    }

    void ShowEnabled()
    {
        MealsGrid.IsVisible = MealsSwitch.IsToggled;
        ExpensesBox.IsVisible = ExpensesSwitch.IsToggled;
    }

    async void OnChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        ShowEnabled();

        ReminderService.MealsOn = MealsSwitch.IsToggled;
        foreach (var t in ReminderService.MealTypes) ReminderService.SetMealMinutes(t, (int)(PickerFor(t).Time ?? TimeSpan.Zero).TotalMinutes);
        ReminderService.ExpensesOn = ExpensesSwitch.IsToggled;
        ReminderService.ExpenseMinutes = (int)(ExpensesPicker.Time ?? TimeSpan.Zero).TotalMinutes;

        if ((ReminderService.MealsOn || ReminderService.ExpensesOn) && !await _reminders.EnsurePermissionAsync())
            StatusLabel.Text = "Notifications are blocked for DayLogger. Allow them in system settings to get reminders.";
        else
            StatusLabel.Text = "";
        await _reminders.RefreshAllAsync();
    }
}
