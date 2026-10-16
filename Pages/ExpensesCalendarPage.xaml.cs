using DayLogger.Controls;
using DayLogger.Data;
using DayLogger.Models;

namespace DayLogger.Pages;

/// <summary>Month calendar of one expense category; a day opens that category's day page.</summary>
[QueryProperty(nameof(CategoryIdText), "cat")]
public partial class ExpensesCalendarPage : ContentPage
{
    readonly ExpenseRepository _expenses;
    readonly CategoryRepository _categories;
    ExpenseCategory? _category;

    public string CategoryIdText { set => _categoryId = int.TryParse(value, out var id) ? id : 0; }
    int _categoryId;

    public ExpensesCalendarPage(ExpenseRepository expenses, CategoryRepository categories)
    {
        InitializeComponent();
        _expenses = expenses;
        _categories = categories;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _category = await _categories.GetAsync(_categoryId);
        if (_category == null) { await Shell.Current.GoToAsync(".."); return; }
        Scaffold.Title = _category.Name;
        Scaffold.Subtitle = "tap a day to log or review";
        await LoadMonthAsync();
        // Data and text are in place before the page flips in, so nothing is rebuilt mid-animation.
        await Scaffold.EnterAsync();
    }

    async Task LoadMonthAsync()
    {
        if (_category == null) return;
        var month = Calendar.Month;
        var expenses = await _expenses.GetMonthAsync(_category.Id, month.Year, month.Month);
        if (month != Calendar.Month) return; // user paged away while loading

        var perDay = expenses.GroupBy(e => int.Parse(e.Date[8..]))
            .ToDictionary(g => g.Key, g => new DayBadge(g.Sum(e => e.Net) != 0 ? Money.Compact(g.Sum(e => e.Net)) : "·", $"{g.Count()}×"));
        Calendar.SetBadges(perDay);

        // Headline is what was really spent; the split shows how much of the month's spending came back.
        var back = expenses.Where(e => e.IsPayback).Sum(e => e.Amount);
        TotalLabel.Text = Money.Format(expenses.Sum(e => e.Net));
        var count = $"{expenses.Count} entr{(expenses.Count == 1 ? "y" : "ies")}";
        SplitLabel.Text = back > 0 ? $"spent {Money.Format(expenses.Where(e => !e.IsPayback).Sum(e => e.Amount))} · back {Money.Format(back)} · {count}" : count;
    }

    async void OnDaySelected(object? sender, DateOnly day) =>
        await Shell.Current.GoToAsync($"expenseday?cat={_categoryId}&date={DateKey.Of(day)}");

    async void OnMonthChanged(object? sender, DateTime month) => await LoadMonthAsync();
}
