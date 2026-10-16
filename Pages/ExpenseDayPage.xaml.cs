using System.Globalization;
using DayLogger.Data;
using DayLogger.Models;

namespace DayLogger.Pages;

[QueryProperty(nameof(DateText), "date")]
public partial class ExpenseDayPage : ContentPage
{
    readonly ExpenseRepository _repo;
    readonly MealRepository _mealRepo;
    DateOnly _day = DateOnly.FromDateTime(DateTime.Today);
    Expense? _editing;
    string? _receipt;          // receipt file currently attached in the form
    string? _newReceipt;       // stored this session but not yet saved; deleted if abandoned

    public List<Expense> Expenses { get; private set; } = new();

    public string DateText
    {
        set
        {
            _day = DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            DateLabel.Text = Scaffold.Title = _day.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture).ToUpperInvariant();
            ResetForm();
        }
    }

    public ExpenseDayPage(ExpenseRepository repo, MealRepository mealRepo)
    {
        InitializeComponent();
        _repo = repo;
        _mealRepo = mealRepo;
        BindingContext = this;
        CaptureButton.IsVisible = MediaPicker.Default.IsCaptureSupported;
        ResetForm();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    async Task RefreshAsync()
    {
        Expenses = await _repo.GetDayAsync(_day);
        OnPropertyChanged(nameof(Expenses));

        var meals = (await _mealRepo.GetDayAsync(_day)).Where(m => m.Price is > 0).ToList();
        var mealTotal = meals.Sum(m => m.Price!.Value);
        MealsCard.IsVisible = meals.Count > 0;
        MealsLabel.Text = $"{meals.Count} meal{(meals.Count == 1 ? "" : "s")}";
        MealsAmountLabel.Text = Money.Format(mealTotal);

        var total = Expenses.Sum(e => e.Amount) + mealTotal;
        SummaryLabel.Text = Scaffold.Subtitle = Expenses.Count + meals.Count == 0 ? "nothing spent" : $"spent {Money.Format(total)}";
    }

    void ResetForm()
    {
        DiscardNewReceipt();
        _editing = null;
        FormTitle.Text = "NEW EXPENSE";
        SaveButton.Text = "SAVE";
        CancelButton.IsVisible = false;
        TitleEntry.Text = AmountEntry.Text = DescriptionEntry.Text = "";
        SetReceipt(null);
    }

    void SetReceipt(string? fileName)
    {
        _receipt = fileName;
        bool has = !string.IsNullOrEmpty(fileName);
        ReceiptBox.IsVisible = RemoveReceiptButton.IsVisible = has;
        ReceiptImage.Source = has ? ImageSource.FromFile(ExpenseRepository.ReceiptPath(fileName!)) : null;
    }

    void DiscardNewReceipt()
    {
        ExpenseRepository.DeleteReceiptFile(_newReceipt);
        _newReceipt = null;
    }

    async void OnCaptureClicked(object? sender, EventArgs e) => await AttachAsync(() => MediaPicker.Default.CapturePhotoAsync());

    async void OnPickClicked(object? sender, EventArgs e) => await AttachAsync(() => MediaPicker.Default.PickPhotoAsync());

    async Task AttachAsync(Func<Task<FileResult?>> get)
    {
        try
        {
            var photo = await get();
            if (photo == null) return;
            DiscardNewReceipt();
            _newReceipt = await ExpenseRepository.StoreReceiptAsync(photo);
            SetReceipt(_newReceipt);
        }
        catch (Exception ex) when (ex is PermissionException or FeatureNotSupportedException)
        {
            await DisplayAlertAsync("Receipt", "Camera or photo access isn't available.", "ok");
        }
    }

    void OnRemoveReceiptClicked(object? sender, EventArgs e)
    {
        // A receipt already saved on the expense is only released when the expense is saved.
        if (_receipt == _newReceipt) DiscardNewReceipt();
        SetReceipt(null);
    }

    async void OnSaveClicked(object? sender, EventArgs e)
    {
        var title = TitleEntry.Text?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            TitleEntry.Focus();
            return;
        }
        if (!double.TryParse(AmountEntry.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
        {
            AmountEntry.Focus();
            return;
        }

        var expense = _editing ?? new Expense { Date = MealRepository.Key(_day) };
        var oldReceipt = expense.Receipt;
        expense.Title = title;
        expense.Amount = amount;
        expense.Description = string.IsNullOrWhiteSpace(DescriptionEntry.Text) ? null : DescriptionEntry.Text.Trim();
        expense.Receipt = _receipt;

        await _repo.SaveAsync(expense);
        if (oldReceipt != expense.Receipt) ExpenseRepository.DeleteReceiptFile(oldReceipt);
        _newReceipt = null; // now owned by the saved expense
        ResetForm();
        await RefreshAsync();
    }

    void OnCancelClicked(object? sender, EventArgs e) => ResetForm();

    // Only one card is open at a time, either showing details (tap) or actions (long-press).
    void Open(Expense? target, bool actions)
    {
        foreach (var other in Expenses.Where(x => x != target)) { other.IsExpanded = false; other.ShowActions = false; }
        if (target == null) return;
        bool wasOpen = actions ? target.ShowActions : target.IsExpanded;
        target.IsExpanded = !actions && !wasOpen;
        target.ShowActions = actions && !wasOpen;
    }

    void OnExpenseTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is Expense expense) Open(expense, actions: false);
    }

    // Long-press (right-click on desktop) flips the card to show its actions.
    void OnExpenseLongPressed(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is Expense expense) Open(expense, actions: true);
    }

    void OnEditAction(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not Expense expense) return;
        expense.ShowActions = false;
        BeginEdit(expense);
    }

    async void OnDeleteAction(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is Expense expense) await DeleteAsync(expense);
    }

    void BeginEdit(Expense expense)
    {
        DiscardNewReceipt();
        _editing = expense;
        FormTitle.Text = "EDIT EXPENSE";
        SaveButton.Text = "UPDATE";
        CancelButton.IsVisible = true;
        TitleEntry.Text = expense.Title;
        AmountEntry.Text = expense.Amount.ToString("0.##", CultureInfo.CurrentCulture);
        DescriptionEntry.Text = expense.Description ?? "";
        SetReceipt(expense.Receipt);
        TitleEntry.Focus(); // brings the form into view
    }

    async Task DeleteAsync(Expense expense)
    {
        if (!await DisplayAlertAsync("Delete expense", expense.Title, "delete", "keep")) return;
        await _repo.DeleteAsync(expense);
        if (_editing?.Id == expense.Id) ResetForm();
        await RefreshAsync();
    }

    async void OnMealsCardTapped(object? sender, TappedEventArgs e) =>
        await Shell.Current.GoToAsync($"mealday?date={MealRepository.Key(_day)}");
}
