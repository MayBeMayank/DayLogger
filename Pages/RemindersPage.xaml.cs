using DayLogger.Data;
using DayLogger.Models;
using DayLogger.Services;

namespace DayLogger.Pages;

/// <summary>One reminder per expense category: a daily nudge, skipped when something is already logged that day.</summary>
public partial class RemindersPage : ContentPage
{
    readonly ReminderService _reminders;
    readonly CategoryRepository _categories;

    public RemindersPage(ReminderService reminders, CategoryRepository categories)
    {
        InitializeComponent();
        _reminders = reminders;
        _categories = categories;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var all = await _categories.GetAllAsync();

        List.Children.Clear();
        if (all.Count == 0)
            List.Children.Add(new Label { Text = "no categories yet: create one from the home pivot (+ new)", Style = (Style)Application.Current!.Resources["Caption"] });
        foreach (var c in all) List.Children.Add(Row(c));
        await Scaffold.EnterAsync();
    }

    View Row(ExpenseCategory category)
    {
        var res = Application.Current!.Resources;
        var toggle = new Switch { IsToggled = category.ReminderOn };
        var picker = new TimePicker { Time = TimeSpan.FromMinutes(category.ReminderMinutes) };
        var box = new Border { Padding = new Thickness(12, 0), Style = (Style)res["ControlBox"], Content = picker, IsVisible = category.ReminderOn };

        async void Changed(object? sender, EventArgs e)
        {
            category.ReminderOn = toggle.IsToggled;
            category.ReminderMinutes = (int)(picker.Time ?? TimeSpan.Zero).TotalMinutes;
            box.IsVisible = category.ReminderOn;
            await _categories.SaveAsync(category);

            StatusLabel.Text = category.ReminderOn && !await _reminders.EnsurePermissionAsync()
                ? "Notifications are blocked for DayLogger. Allow them in system settings to get reminders."
                : "";
            await _reminders.RefreshAllAsync();
        }
        toggle.Toggled += Changed;
        picker.TimeSelected += Changed;

        var head = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        head.Add(new Label { Text = category.Name.ToUpperInvariant(), Style = (Style)res["Caption"], VerticalOptions = LayoutOptions.Center });
        head.Add(toggle, 1);

        return new Border
        {
            Padding = 12,
            Style = (Style)res["Section"],
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    head,
                    new Label { Text = $"Asks you to log in {category.Name.ToLowerInvariant()} once a day, unless you already have.", Style = (Style)res["Caption"] },
                    box,
                },
            },
        };
    }
}
