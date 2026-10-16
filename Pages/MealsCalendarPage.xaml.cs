using DayLogger.Controls;
using DayLogger.Data;
using DayLogger.Models;

namespace DayLogger.Pages;

public partial class MealsCalendarPage : ContentPage
{
    readonly MealRepository _repo;

    public MealsCalendarPage(MealRepository repo)
    {
        InitializeComponent();
        _repo = repo;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadMonthAsync();
    }

    async Task LoadMonthAsync()
    {
        var month = Calendar.Month;
        var meals = await _repo.GetMonthAsync(month.Year, month.Month);
        if (month != Calendar.Month) return; // user paged away while loading

        var badges = meals
            .GroupBy(m => int.Parse(m.Date[8..]))
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var total = g.Sum(m => m.Price ?? 0);
                    return new DayBadge(new string('●', Math.Min(g.Count(), 4)), total > 0 ? Money.Compact(total) : null);
                });
        Calendar.SetBadges(badges);

        var spent = meals.Sum(m => m.Price ?? 0);
        var days = meals.Select(m => m.Date).Distinct().Count();
        TotalsLabel.Text = $"{meals.Count} meals · {days} days · spent {Money.Format(spent)}";
        SourcesLabel.Text = meals.Count == 0
            ? "nothing logged yet"
            : string.Join(" · ", Enum.GetValues<MealSource>()
                .Select(s => (s, n: meals.Count(m => m.Source == s)))
                .Where(x => x.n > 0)
                .Select(x => $"{x.s.ToString().ToLowerInvariant()} {x.n}"));
    }


    async void OnDaySelected(object? sender, DateOnly day) =>
        await Shell.Current.GoToAsync($"mealday?date={MealRepository.Key(day)}");

    async void OnMonthChanged(object? sender, DateTime month) => await LoadMonthAsync();
}
