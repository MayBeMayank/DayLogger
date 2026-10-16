using System.Globalization;
using DayLogger.Data;
using DayLogger.Models;

namespace DayLogger.Pages;

[QueryProperty(nameof(DateText), "date")]
public partial class MealDayPage : ContentPage
{
    const string LastSourcePref = "meals.lastSource";

    readonly MealRepository _repo;
    DateOnly _day = DateOnly.FromDateTime(DateTime.Today);
    Meal? _editing;

    public List<Meal> Meals { get; private set; } = new();

    public string DateText
    {
        set
        {
            _day = DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            DateLabel.Text = Scaffold.Title = _day.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture).ToUpperInvariant();
            ResetForm();
        }
    }

    public MealDayPage(MealRepository repo)
    {
        InitializeComponent();
        _repo = repo;
        BindingContext = this;
        TypeChips.SetItems(Enum.GetValues<MealType>().Select(t => $"{Icon(t)} {t.ToString().ToLowerInvariant()}"));
        SourceChips.SetItems(Enum.GetValues<MealSource>().Select(s => $"{Icon(s)} {s.ToString().ToLowerInvariant()}"));
        ResetForm();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    async Task RefreshAsync()
    {
        Meals = await _repo.GetDayAsync(_day);
        OnPropertyChanged(nameof(Meals));
        var spent = Meals.Sum(m => m.Price ?? 0);
        SummaryLabel.Text = Scaffold.Subtitle = Meals.Count == 0 ? "no meals logged" : $"{Meals.Count} meals · spent {Money.Format(spent)}";
    }

    void ResetForm()
    {
        _editing = null;
        FormTitle.Text = "NEW MEAL";
        SaveButton.Text = "SAVE";
        CancelButton.IsVisible = false;
        DishesEntry.Text = PriceEntry.Text = NoteEntry.Text = "";
        TypeChips.SelectedIndex = (int)GuessType();
        SourceChips.SelectedIndex = Preferences.Get(LastSourcePref, (int)MealSource.Cooked);
    }

    static string Icon(MealType t) => t switch { MealType.Breakfast => "🍳", MealType.Lunch => "🥗", MealType.Dinner => "🍽️", _ => "🍪" };
    static string Icon(MealSource s) => s switch { MealSource.Cooked => "🏠", MealSource.Online => "🛵", MealSource.Outside => "🍴", _ => "📦" };

    MealType GuessType()
    {
        if (_day != DateOnly.FromDateTime(DateTime.Today)) return MealType.Lunch;
        var h = DateTime.Now.Hour;
        return h < 11 ? MealType.Breakfast : h < 16 ? MealType.Lunch : h < 18 ? MealType.Snack : MealType.Dinner;
    }

    async void OnSaveClicked(object? sender, EventArgs e)
    {
        var dishes = DishesEntry.Text?.Trim();
        if (string.IsNullOrEmpty(dishes))
        {
            DishesEntry.Focus();
            return;
        }

        var meal = _editing ?? new Meal { Date = MealRepository.Key(_day) };
        meal.Type = (MealType)TypeChips.SelectedIndex;
        meal.Source = (MealSource)SourceChips.SelectedIndex;
        meal.Dishes = dishes;
        meal.Price = double.TryParse(PriceEntry.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var p) && p > 0 ? p : null;
        meal.Note = string.IsNullOrWhiteSpace(NoteEntry.Text) ? null : NoteEntry.Text.Trim();

        await _repo.SaveAsync(meal);
        Preferences.Set(LastSourcePref, (int)meal.Source);
        ResetForm();
        await RefreshAsync();
    }

    void OnCancelClicked(object? sender, EventArgs e) => ResetForm();

    // Long-press (right-click on desktop) flips the card to show its actions.
    void OnMealLongPressed(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not Meal meal) return;
        foreach (var other in Meals.Where(x => x != meal)) other.ShowActions = false; // one open card at a time
        meal.ShowActions = !meal.ShowActions;
    }

    void OnEditAction(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not Meal meal) return;
        meal.ShowActions = false;
        BeginEdit(meal);
    }

    async void OnDeleteAction(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is Meal meal) await DeleteAsync(meal);
    }

    void BeginEdit(Meal meal)
    {
        _editing = meal;
        FormTitle.Text = "EDIT MEAL";
        SaveButton.Text = "UPDATE";
        CancelButton.IsVisible = true;
        TypeChips.SelectedIndex = (int)meal.Type;
        SourceChips.SelectedIndex = (int)meal.Source;
        DishesEntry.Text = meal.Dishes;
        PriceEntry.Text = meal.Price is > 0 ? meal.Price.Value.ToString("0.##", CultureInfo.CurrentCulture) : "";
        NoteEntry.Text = meal.Note ?? "";
    }

    async Task DeleteAsync(Meal meal)
    {
        if (!await DisplayAlertAsync("Delete meal", meal.Dishes, "delete", "keep")) return;
        await _repo.DeleteAsync(meal);
        if (_editing?.Id == meal.Id) ResetForm();
        await RefreshAsync();
    }
}
