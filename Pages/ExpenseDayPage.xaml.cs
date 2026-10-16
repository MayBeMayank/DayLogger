using DayLogger.Controls;
using System.Globalization;
using DayLogger.Data;
using DayLogger.Models;

namespace DayLogger.Pages;

[QueryProperty(nameof(DateText), "date")]
[QueryProperty(nameof(CategoryIdText), "cat")]
[QueryProperty(nameof(AddText), "add")]
public partial class ExpenseDayPage : ContentPage
{
    const int MaxSuggestions = 8;

    readonly ExpenseRepository _repo;
    readonly CategoryRepository _categories;
    DateOnly _day = DateOnly.FromDateTime(DateTime.Today);
    int _categoryId;
    bool _focusForm;
    ExpenseCategory? _category;
    List<ExpenseSubcategory> _subs = new();
    List<string> _knownTags = new();
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
        }
    }

    public string CategoryIdText { set => _categoryId = int.TryParse(value, out var id) ? id : 0; }

    /// <summary>"+" on the home pivot opens this page with the form ready for typing.</summary>
    public string AddText { set => _focusForm = value == "1"; }

    public ExpenseDayPage(ExpenseRepository repo, CategoryRepository categories)
    {
        InitializeComponent();
        _repo = repo;
        _categories = categories;
        BindingContext = this;
        CaptureButton.IsVisible = MediaPicker.Default.IsCaptureSupported;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_category?.Id != _categoryId)
        {
            _category = await _categories.GetAsync(_categoryId);
            if (_category == null) { await Shell.Current.GoToAsync(".."); return; }
            _subs = await _categories.GetSubcategoriesAsync(_category.Id);
            SubChips.SetItems(_subs.Select(s => s.Name.ToLowerInvariant()));
            SubChips.IsVisible = _subs.Count > 0;
            AmountEntry.Placeholder = _category.AmountRequired ? "amount" : "amount (optional)";
            ResetForm();
        }

        _knownTags = await _repo.GetTagSuggestionsAsync();
        ShowSuggestions();
        await RefreshAsync();

        if (_focusForm)
        {
            _focusForm = false;
            TitleEntry.FocusSoon();
        }
    }

    async Task RefreshAsync()
    {
        if (_category == null) return;
        Expenses = await _repo.GetDayAsync(_category.Id, _day);
        OnPropertyChanged(nameof(Expenses));

        var total = Expenses.Sum(e => e.Amount);
        var summary = Expenses.Count == 0 ? "nothing logged" : total > 0 ? $"{Expenses.Count}× · {Money.Format(total)}" : $"{Expenses.Count}×";
        Scaffold.Subtitle = $"{_category.Name.ToLowerInvariant()} · {summary}";
        SummaryLabel.Text = Scaffold.Subtitle;
    }

    string LastSubPref => $"expenses.lastSub.{_categoryId}";

    void ResetForm()
    {
        DiscardNewReceipt();
        _editing = null;
        FormTitle.Text = "NEW ENTRY";
        SaveButton.Text = "SAVE";
        CancelButton.IsVisible = false;
        TitleEntry.Text = AmountEntry.Text = DescriptionEntry.Text = TagsEntry.Text = "";
        // A new entry starts on the subcategory used last time in this category.
        var last = Preferences.Get(LastSubPref, 0);
        SubChips.SelectedIndex = _subs.FindIndex(s => s.Id == last);
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

    // ---- tags: free text with suggestions from tags used before ----

    void OnTagsChanged(object? sender, TextChangedEventArgs e) => ShowSuggestions();

    /// <summary>Tags already complete in the text (before the last separator) and the half-typed one after it.</summary>
    (string Prefix, List<string> Done, string Token) SplitTags()
    {
        var text = TagsEntry.Text ?? "";
        int cut = text.LastIndexOfAny(new[] { ',', ';', ' ' }) + 1;
        return (text[..cut], ExpenseRepository.ParseTags(text[..cut]), ExpenseRepository.NormalizeTag(text[cut..]));
    }

    void ShowSuggestions()
    {
        var (_, done, token) = SplitTags();
        var matches = _knownTags.Where(t => !done.Contains(t) && t != token && t.StartsWith(token, StringComparison.Ordinal)).Take(MaxSuggestions).ToList();

        TagSuggestions.Children.Clear();
        foreach (var tag in matches)
        {
            var label = new Label { Text = "#" + tag, FontSize = 13, Padding = new Thickness(10, 7) };
            var chip = new Border { Content = label, Margin = new Thickness(0, 0, 6, 6), Style = (Style)Application.Current!.Resources["ControlBox"] };
            chip.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => AcceptSuggestion(tag)) });
            TagSuggestions.Children.Add(chip);
        }
    }

    void AcceptSuggestion(string tag)
    {
        var (_, done, _) = SplitTags();
        done.Add(tag);
        TagsEntry.Text = string.Join(", ", done) + ", ";
        TagsEntry.CursorPosition = TagsEntry.Text.Length;
    }

    // ---- receipt ----

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

    // ---- save / edit / delete ----

    async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_category == null) return;

        var title = TitleEntry.Text?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            TitleEntry.Focus();
            return;
        }

        // Optional amounts may be left empty (stored as 0); anything typed has to be a valid number.
        double amount = 0;
        bool blank = string.IsNullOrWhiteSpace(AmountEntry.Text);
        bool valid = blank
            ? !_category.AmountRequired
            : double.TryParse(AmountEntry.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out amount) && amount >= 0 && (amount > 0 || !_category.AmountRequired);
        if (!valid)
        {
            AmountEntry.Focus();
            return;
        }

        var expense = _editing ?? new Expense { Date = DateKey.Of(_day), CategoryId = _category.Id };
        var oldReceipt = expense.Receipt;
        expense.Title = title;
        expense.Amount = amount;
        expense.Description = string.IsNullOrWhiteSpace(DescriptionEntry.Text) ? null : DescriptionEntry.Text.Trim();
        expense.Receipt = _receipt;
        expense.SubcategoryId = SubChips.SelectedIndex >= 0 ? _subs[SubChips.SelectedIndex].Id : null;
        expense.Tags = ExpenseRepository.ParseTags(TagsEntry.Text);

        await _repo.SaveAsync(expense);
        if (oldReceipt != expense.Receipt) ExpenseRepository.DeleteReceiptFile(oldReceipt);
        _newReceipt = null; // now owned by the saved expense
        Preferences.Set(LastSubPref, expense.SubcategoryId ?? 0);
        _knownTags = await _repo.GetTagSuggestionsAsync();
        ResetForm();
        await RefreshAsync();
    }

    void OnCancelClicked(object? sender, EventArgs e) => ResetForm();

    // Only one card shows its details at a time.
    void Open(Expense target)
    {
        foreach (var other in Expenses.Where(x => x != target)) other.IsExpanded = false;
        target.IsExpanded = !target.IsExpanded;
    }

    void OnExpenseTapped(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is Expense expense) Open(expense);
    }

    // Press-and-hold (right-click on desktop) opens the entry's menu.
    void OnExpenseLongPressed(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not Expense expense) return;
        Menu.Show(expense.Title,
            new BarAction("\u270E", "edit", () => { BeginEdit(expense); return Task.CompletedTask; }),
            new BarAction("\U0001F5D1", "delete", () => DeleteAsync(expense)));
    }

    // Android's back button closes the menu before it leaves the page.
    protected override bool OnBackButtonPressed()
    {
        if (!Menu.IsOpen) return base.OnBackButtonPressed();
        _ = Menu.CloseAsync();
        return true;
    }

    void BeginEdit(Expense expense)
    {
        DiscardNewReceipt();
        _editing = expense;
        FormTitle.Text = "EDIT ENTRY";
        SaveButton.Text = "UPDATE";
        CancelButton.IsVisible = true;
        TitleEntry.Text = expense.Title;
        AmountEntry.Text = expense.Amount > 0 ? expense.Amount.ToString("0.##", CultureInfo.CurrentCulture) : "";
        DescriptionEntry.Text = expense.Description ?? "";
        TagsEntry.Text = string.Join(", ", expense.Tags);
        SubChips.SelectedIndex = _subs.FindIndex(s => s.Id == expense.SubcategoryId);
        SetReceipt(expense.Receipt);
        TitleEntry.Focus(); // brings the form into view
    }

    async Task DeleteAsync(Expense expense)
    {
        if (!await DisplayAlertAsync("Delete entry", expense.Title, "delete", "keep")) return;
        await _repo.DeleteAsync(expense);
        if (_editing?.Id == expense.Id) ResetForm();
        _knownTags = await _repo.GetTagSuggestionsAsync();
        ShowSuggestions();
        await RefreshAsync();
    }
}
