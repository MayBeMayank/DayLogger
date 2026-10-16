using DayLogger.Controls;
using System.Globalization;
using DayLogger.Data;
using DayLogger.Models;
using DayLogger.Services;

namespace DayLogger.Pages;

[QueryProperty(nameof(DateText), "date")]
[QueryProperty(nameof(CategoryIdText), "cat")]
[QueryProperty(nameof(AddText), "add")]
[QueryProperty(nameof(PaybackText), "payback")]
public partial class ExpenseDayPage : ContentPage
{
    const int MaxSuggestions = 8;

    readonly ExpenseRepository _repo;
    readonly CategoryRepository _categories;
    readonly PersonRepository _peopleRepo;
    DateOnly _day = DateOnly.FromDateTime(DateTime.Today);
    int _categoryId;
    bool _focusForm;
    ExpenseCategory? _category;
    List<ExpenseSubcategory> _subs = new();
    List<string> _knownTags = new();
    List<Person> _people = new();
    string? _pickedName, _pickedPhone;
    byte[]? _pickedPhoto;   // a contact chosen from the phone, saved with the person if the name is still it
    Expense? _editing;
    bool _isPayback;           // the form's "got back" side is active
    List<Expense> _links = new();   // spends the chips in LinkChips stand for
    bool _suppressLink;
    long? _pendingPayback;
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

    /// <summary>CreatedTicks of a spend to record a payback for, set when arriving from another day's page.</summary>
    public string PaybackText { set => _pendingPayback = long.TryParse(value, out var t) ? t : null; }

    public ExpenseDayPage(ExpenseRepository repo, CategoryRepository categories, PersonRepository people)
    {
        InitializeComponent();
        _repo = repo;
        _categories = categories;
        _peopleRepo = people;
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
            ResetForm();
        }

        _knownTags = await _repo.GetTagSuggestionsAsync();
        ShowSuggestions();
        _people = await _peopleRepo.GetAllAsync();
        ShowPersonSuggestions();
        await RefreshAsync();
        await Scaffold.EnterAsync();

        if (_pendingPayback is { } pending)
        {
            _pendingPayback = null;
            await BeginPaybackAsync(pending);
        }
        else if (_focusForm)
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

        var total = Expenses.Sum(e => e.Net);
        var summary = Expenses.Count == 0 ? "nothing logged" : total != 0 ? $"{Expenses.Count}× · {Money.Format(total)}" : $"{Expenses.Count}×";
        Scaffold.Subtitle = $"{_category.Name.ToLowerInvariant()} · {summary}";
        SummaryLabel.Text = Scaffold.Subtitle;
        await RebuildLinksAsync(_editing?.SettlesTicks);
    }

    // ---- spent | got back ----

    void OnSpentTabTapped(object? sender, TappedEventArgs e) => SetMode(false);
    void OnBackTabTapped(object? sender, TappedEventArgs e) => SetMode(true);

    void SetMode(bool payback)
    {
        _isPayback = payback;
        SpentTab.SetDynamicResource(Label.TextColorProperty, payback ? "Muted" : "Ink");
        BackTab.SetDynamicResource(Label.TextColorProperty, payback ? "Ink" : "Muted");
        SpentTab.FontSize = payback ? 18 : 26;
        BackTab.FontSize = payback ? 26 : 18;
        LinkBox.IsVisible = payback;
        ExpectRow.IsVisible = !payback;
        FormTitle.Text = (_editing == null ? "NEW " : "EDIT ") + (payback ? "PAYBACK" : "ENTRY");
        TitleEntry.Placeholder = payback ? "what came back, e.g. refund" : "what for, e.g. groceries";
        AmountEntry.Placeholder = payback ? "amount back" : _category?.AmountRequired == false ? "amount (optional)" : "amount";
    }

    /// <summary>Reloads the spends a payback can settle, keeping the current choice (or showing <paramref name="include"/>).</summary>
    async Task RebuildLinksAsync(long? include)
    {
        if (_category == null) return;
        var chosen = LinkChips.SelectedIndex >= 0 && LinkChips.SelectedIndex < _links.Count ? _links[LinkChips.SelectedIndex].CreatedTicks : include;
        _links = await _repo.GetLinkCandidatesAsync(_category.Id, chosen ?? include);
        _suppressLink = true;
        LinkChips.SetItems(_links.Select(l => l.Open > 0
            ? $"{l.Title.ToLowerInvariant()} · {Money.Format(l.Open)} open"
            : $"{l.Title.ToLowerInvariant()} · {Money.Format(Math.Max(0, l.Amount - l.BackTotal))} left"));
        LinkChips.SelectedIndex = chosen is { } t ? _links.FindIndex(l => l.CreatedTicks == t) : -1;
        _suppressLink = false;
    }

    void SelectLink(long? ticks)
    {
        _suppressLink = true;
        LinkChips.SelectedIndex = ticks is { } t ? _links.FindIndex(l => l.CreatedTicks == t) : -1;
        _suppressLink = false;
    }

    // Choosing what a payback settles fills in what can be guessed, without overwriting anything already typed.
    void OnLinkChanged(object? sender, EventArgs e)
    {
        if (_suppressLink || LinkChips.SelectedIndex < 0 || LinkChips.SelectedIndex >= _links.Count) return;
        var link = _links[LinkChips.SelectedIndex];
        var left = link.Open > 0 ? link.Open : Math.Max(0, link.Amount - link.BackTotal);
        if (string.IsNullOrWhiteSpace(AmountEntry.Text) && left > 0) AmountEntry.Text = left.ToString("0.##", CultureInfo.CurrentCulture);
        if (string.IsNullOrWhiteSpace(TitleEntry.Text)) TitleEntry.Text = "back · " + link.Title;
        if (string.IsNullOrWhiteSpace(PersonEntry.Text) && link.PersonName != null) PersonEntry.Text = link.PersonName;
        if (string.IsNullOrWhiteSpace(TagsEntry.Text)) TagsEntry.Text = string.Join(", ", link.Tags);
        var sub = _subs.FindIndex(s => s.Id == link.SubcategoryId);
        if (sub >= 0) SubChips.SelectedIndex = sub;
    }

    async Task BeginPaybackAsync(long spendTicks)
    {
        ResetForm();
        SetMode(true);
        await RebuildLinksAsync(spendTicks);
        LinkChips.SelectedIndex = _links.FindIndex(l => l.CreatedTicks == spendTicks); // fills the form via OnLinkChanged
        AmountEntry.FocusSoon();
    }

    // A payback is dated on the day being viewed, so from another day's page it opens today's page instead.
    Task StartPaybackAsync(Expense spend)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return _day == today
            ? BeginPaybackAsync(spend.CreatedTicks)
            : Shell.Current.GoToAsync($"expenseday?cat={_categoryId}&date={DateKey.Of(today)}&payback={spend.CreatedTicks}");
    }

    async Task ToggleWrittenOffAsync(Expense spend)
    {
        spend.WrittenOff = !spend.WrittenOff;
        await _repo.SaveAsync(spend);
        await RefreshAsync();
    }

    string LastSubPref => $"expenses.lastSub.{_categoryId}";

    void ResetForm()
    {
        DiscardNewReceipt();
        _editing = null;
        ExpectSwitch.IsToggled = false;
        SetMode(false);
        SaveButton.Text = "SAVE";
        CancelButton.IsVisible = false;
        TitleEntry.Text = AmountEntry.Text = DescriptionEntry.Text = TagsEntry.Text = PersonEntry.Text = "";
        _pickedName = _pickedPhone = null;
        _pickedPhoto = null;
        // A new entry starts on the subcategory used last time in this category.
        var last = Preferences.Get(LastSubPref, 0);
        SubChips.SelectedIndex = _subs.FindIndex(s => s.Id == last);
        SetReceipt(null);
        SelectLink(null);
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

    // ---- who it was with: typed (a new name adds a person) or picked from the phone's contacts ----

    void OnPersonChanged(object? sender, TextChangedEventArgs e) => ShowPersonSuggestions();

    void ShowPersonSuggestions()
    {
        var token = (PersonEntry.Text ?? "").Trim();
        PersonAvatar.Name = token;
        PersonAvatar.Photo = _people.FirstOrDefault(p => p.Name.Equals(token, StringComparison.OrdinalIgnoreCase))?.Photo;
        var matches = _people.Where(p => !p.Name.Equals(token, StringComparison.OrdinalIgnoreCase)
                && (token.Length == 0 || p.Name.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .Take(MaxSuggestions).ToList();

        PersonSuggestions.Children.Clear();
        foreach (var person in matches)
        {
            var label = new Label { Text = person.Name.ToLowerInvariant(), FontSize = 13, Padding = new Thickness(10, 7) };
            var chip = new Border { Content = label, Margin = new Thickness(0, 0, 6, 6), Style = (Style)Application.Current!.Resources["ControlBox"] };
            chip.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => PersonEntry.Text = person.Name) });
            PersonSuggestions.Children.Add(chip);
        }
    }

    async void OnPickContactClicked(object? sender, EventArgs e)
    {
        try
        {
            // READ_CONTACTS is a runtime permission: declaring it in the manifest isn't enough, and the picker's phone lookup fails without it.
            var status = await Permissions.CheckStatusAsync<Permissions.ContactsRead>();
            if (status != PermissionStatus.Granted) status = await Permissions.RequestAsync<Permissions.ContactsRead>();
            if (status != PermissionStatus.Granted)
            {
                await DisplayAlertAsync("Contacts", "Contacts permission was not granted. You can still type a name.", "ok");
                return;
            }

            var contact = await Contacts.Default.PickContactAsync();
            if (contact == null || string.IsNullOrWhiteSpace(contact.DisplayName)) return;
            PersonEntry.Text = _pickedName = contact.DisplayName.Trim();
            _pickedPhone = contact.Phones.FirstOrDefault()?.PhoneNumber;
            _pickedPhoto = ContactPhoto.Read(contact.Id);
        }
        catch (Exception ex) when (ex is PermissionException or FeatureNotSupportedException)
        {
            await DisplayAlertAsync("Contacts", "Contacts aren't available here. You can still type a name.", "ok");
        }
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
        bool required = _isPayback || _category.AmountRequired; // money coming back always has an amount
        bool valid = blank
            ? !required
            : double.TryParse(AmountEntry.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out amount) && amount >= 0 && (amount > 0 || !required);
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
        var who = PersonEntry.Text?.Trim();
        expense.PersonId = string.IsNullOrEmpty(who)
            ? null
            : (await _peopleRepo.FindOrCreateAsync(who, who == _pickedName ? _pickedPhone : null, who == _pickedName ? _pickedPhoto : null)).Id;
        expense.IsPayback = _isPayback;
        expense.SettlesTicks = _isPayback && LinkChips.SelectedIndex >= 0 && LinkChips.SelectedIndex < _links.Count ? _links[LinkChips.SelectedIndex].CreatedTicks : null;
        expense.ExpectBack = !_isPayback && ExpectSwitch.IsToggled;
        if (!expense.ExpectBack) expense.WrittenOff = false;

        await _repo.SaveAsync(expense);
        if (oldReceipt != expense.Receipt) ExpenseRepository.DeleteReceiptFile(oldReceipt);
        _newReceipt = null; // now owned by the saved expense
        Preferences.Set(LastSubPref, expense.SubcategoryId ?? 0);
        _knownTags = await _repo.GetTagSuggestionsAsync();
        _people = await _peopleRepo.GetAllAsync();
        ResetForm();
        ShowPersonSuggestions();
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
        var actions = new List<BarAction> { new("\u270E", "edit", () => BeginEditAsync(expense)) };
        if (!expense.IsPayback)
        {
            actions.Add(new("\u21A9", "got back", () => StartPaybackAsync(expense)));
            if (expense.ExpectBack && (expense.Open > 0 || expense.WrittenOff))
                actions.Add(new("\u2713", expense.WrittenOff ? "reopen" : "write off", () => ToggleWrittenOffAsync(expense)));
        }
        actions.Add(new("\U0001F5D1", "delete", () => DeleteAsync(expense)));
        Menu.Show(expense.Title, actions.ToArray());
    }

    // Android's back button closes the menu before it leaves the page.
    protected override bool OnBackButtonPressed()
    {
        if (!Menu.IsOpen) return base.OnBackButtonPressed();
        _ = Menu.CloseAsync();
        return true;
    }

    async Task BeginEditAsync(Expense expense)
    {
        DiscardNewReceipt();
        _editing = expense;
        SetMode(expense.IsPayback);
        ExpectSwitch.IsToggled = expense.ExpectBack;
        SaveButton.Text = "UPDATE";
        CancelButton.IsVisible = true;
        TitleEntry.Text = expense.Title;
        AmountEntry.Text = expense.Amount > 0 ? expense.Amount.ToString("0.##", CultureInfo.CurrentCulture) : "";
        DescriptionEntry.Text = expense.Description ?? "";
        TagsEntry.Text = string.Join(", ", expense.Tags);
        PersonEntry.Text = expense.PersonName ?? "";
        SubChips.SelectedIndex = _subs.FindIndex(s => s.Id == expense.SubcategoryId);
        SetReceipt(expense.Receipt);
        await RebuildLinksAsync(expense.SettlesTicks);
        SelectLink(expense.SettlesTicks);
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
