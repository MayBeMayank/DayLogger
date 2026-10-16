using DayLogger.Controls;
using DayLogger.Data;
using DayLogger.Models;

namespace DayLogger.Pages;

public partial class ExpensesCalendarPage : ContentPage
{
    readonly ExpenseRepository _expenses;
    readonly MealRepository _meals;

    public ExpensesCalendarPage(ExpenseRepository expenses, MealRepository meals)
    {
        InitializeComponent();
        _expenses = expenses;
        _meals = meals;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadMonthAsync();
    }

    async Task LoadMonthAsync()
    {
        var month = Calendar.Month;
        var expenses = await _expenses.GetMonthAsync(month.Year, month.Month);
        var meals = (await _meals.GetMonthAsync(month.Year, month.Month)).Where(m => m.Price is > 0).ToList();
        if (month != Calendar.Month) return; // user paged away while loading

        // One (amount, count) entry per day, from both sources.
        var perDay = expenses.Select(e => (e.Date, Amount: e.Amount))
            .Concat(meals.Select(m => (m.Date, Amount: m.Price!.Value)))
            .GroupBy(x => int.Parse(x.Date[8..]))
            .ToDictionary(g => g.Key, g => new DayBadge(Money.Compact(g.Sum(x => x.Amount)), $"{g.Count()}×"));
        Calendar.SetBadges(perDay);

        var spentExpenses = expenses.Sum(e => e.Amount);
        var spentMeals = meals.Sum(m => m.Price!.Value);
        TotalLabel.Text = Money.Format(spentExpenses + spentMeals);
        SplitLabel.Text = $"expenses {Money.Format(spentExpenses)} · meals {Money.Format(spentMeals)}";
    }


    async void OnDaySelected(object? sender, DateOnly day) =>
        await Shell.Current.GoToAsync($"expenseday?date={MealRepository.Key(day)}");

    async void OnMonthChanged(object? sender, DateTime month) => await LoadMonthAsync();
}
