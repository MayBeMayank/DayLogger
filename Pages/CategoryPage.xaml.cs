using DayLogger.Controls;
using DayLogger.Data;
using DayLogger.Models;
using DayLogger.Services;

namespace DayLogger.Pages;

/// <summary>Edits one expense category (id 0 creates a new one): name, whether an amount is required, its subcategories, position, archive and delete.</summary>
[QueryProperty(nameof(IdText), "id")]
public partial class CategoryPage : ContentPage
{
    class SubRow
    {
        public int Id;
        public string Name = "";
        public Entry? Entry;
    }

    readonly CategoryRepository _categories;
    readonly ReminderService _reminders;
    ExpenseCategory? _category;
    int _id;
    readonly List<SubRow> _rows = new();

    bool _newShown;

    public string IdText { set => _id = int.TryParse(value, out var id) ? id : 0; }

    bool IsNew => _id == 0;

    public CategoryPage(CategoryRepository categories, ReminderService reminders)
    {
        InitializeComponent();
        _categories = categories;
        _reminders = reminders;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (IsNew)
        {
            if (_newShown) return; // coming back from a dialog, keep what's typed
            _newShown = true;
            Scaffold.Title = "new category";
            Scaffold.Subtitle = "its own row in the expenses list";
            ExistingOnly.IsVisible = false;
            AmountSwitch.IsToggled = true;
            NameEntry.FocusSoon();
            return;
        }
        if (_category?.Id == _id) return; // coming back from a dialog, keep what's typed
        await LoadAsync();
    }

    async Task LoadAsync()
    {
        _category = await _categories.GetAsync(_id);
        if (_category == null) { await Shell.Current.GoToAsync(".."); return; }

        Scaffold.Title = _category.Name;
        NameEntry.Text = _category.Name;
        AmountSwitch.IsToggled = _category.AmountRequired;
        ArchiveButton.Text = _category.Archived ? "RESTORE" : "ARCHIVE";
        StatusLabel.Text = _category.Archived ? "archived: hidden from the expenses list, entries kept" : "";

        _rows.Clear();
        foreach (var s in await _categories.GetSubcategoriesAsync(_id)) _rows.Add(new SubRow { Id = s.Id, Name = s.Name });
        BuildRows();
    }

    void SyncNames()
    {
        foreach (var row in _rows) if (row.Entry != null) row.Name = row.Entry.Text ?? "";
    }

    void BuildRows()
    {
        SubList.Children.Clear();
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            int at = i;
            row.Entry = new Entry { Text = row.Name, Placeholder = "name" };
            var box = new Border { Padding = new Thickness(12, 6), Style = (Style)Application.Current!.Resources["ControlBox"], Content = row.Entry };

            View Tool(string glyph, Action run)
            {
                var l = new Label { Text = glyph, WidthRequest = 40, HeightRequest = 44, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center, FontSize = 18 };
                l.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(run) });
                return l;
            }

            var grid = new Grid { ColumnSpacing = 0 };
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            for (int c = 0; c < 3; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.Add(box, 0);
            grid.Add(Tool("↑", () => MoveRow(at, -1)), 1);
            grid.Add(Tool("↓", () => MoveRow(at, +1)), 2);
            grid.Add(Tool("✕", () => { SyncNames(); _rows.RemoveAt(at); BuildRows(); }), 3);
            SubList.Children.Add(grid);
        }
    }

    void MoveRow(int at, int delta)
    {
        SyncNames();
        int to = at + delta;
        if (to < 0 || to >= _rows.Count) return;
        (_rows[at], _rows[to]) = (_rows[to], _rows[at]);
        BuildRows();
    }

    void OnAddSubClicked(object? sender, EventArgs e)
    {
        if (!AddTypedSubcategories()) NewSubEntry.Focus();
    }

    /// <summary>Turns "breakfast, lunch, dinner" in the add box into rows. Names already in the list are skipped.</summary>
    bool AddTypedSubcategories()
    {
        var typed = (NewSubEntry.Text ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (typed.Length == 0) return false;

        SyncNames();
        foreach (var name in typed)
            if (!_rows.Any(r => string.Equals(r.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
                _rows.Add(new SubRow { Name = name });
        NewSubEntry.Text = "";
        BuildRows();
        return true;
    }

    async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_category == null && !IsNew) return;
        SyncNames();
        AddTypedSubcategories(); // names typed but not added yet still count

        var name = NameEntry.Text?.Trim();
        if (string.IsNullOrEmpty(name)) { NameEntry.Focus(); return; }

        var others = (await _categories.GetAllAsync(includeArchived: true)).Where(c => c == null || c.Id != _category?.Id);
        if (others.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            StatusLabel.Text = "there is already a category with that name";
            return;
        }

        var names = _rows.Select(r => r.Name.Trim()).Where(n => n.Length > 0).ToList();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
        {
            StatusLabel.Text = "two subcategories have the same name";
            return;
        }

        if (IsNew)
        {
            await _categories.CreateAsync(name, AmountSwitch.IsToggled, names);
            await Shell.Current.GoToAsync("..");
            return;
        }

        _category!.Name = name;
        _category.AmountRequired = AmountSwitch.IsToggled;
        await _categories.SaveAsync(_category);
        await _categories.SaveSubcategoriesAsync(_category.Id, _rows.Where(r => r.Name.Trim().Length > 0).Select(r => (r.Id, r.Name)).ToList());

        Scaffold.Title = name;
        StatusLabel.Text = "saved";
        await LoadAsync();
    }

    async void OnEarlierClicked(object? sender, EventArgs e) => await MoveAsync(-1);

    async void OnLaterClicked(object? sender, EventArgs e) => await MoveAsync(+1);

    async Task MoveAsync(int delta)
    {
        if (_category == null) return;
        StatusLabel.Text = await _categories.MoveAsync(_category, delta) ? "moved" : delta < 0 ? "already first" : "already last";
    }

    async void OnArchiveClicked(object? sender, EventArgs e)
    {
        if (_category == null) return;
        bool archive = !_category.Archived;
        if (archive && !await DisplayAlertAsync("Archive category", $"“{_category.Name}” disappears from the expenses list. Its entries are kept and you can restore it from there.", "archive", "cancel")) return;

        await _categories.SetArchivedAsync(_category, archive);
        await _reminders.RefreshAllAsync();
        await Shell.Current.GoToAsync("..");
    }

    async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (_category == null) return;

        int entries = await _categories.CountEntriesAsync(_category.Id);
        var others = (await _categories.GetAllAsync(includeArchived: true)).Where(c => c.Id != _category.Id).ToList();

        if (entries == 0)
        {
            if (!await DisplayAlertAsync("Delete category", $"Delete “{_category.Name}”?", "delete", "cancel")) return;
            await _categories.DeleteWithEntriesAsync(_category);
        }
        else
        {
            var move = "Move the entries to another category";
            var buttons = others.Count > 0 ? new[] { move } : Array.Empty<string>();
            var choice = await DisplayActionSheetAsync($"“{_category.Name}” has {entries} entr{(entries == 1 ? "y" : "ies")}", "cancel", $"Delete category and its {entries} entr{(entries == 1 ? "y" : "ies")}", buttons);

            if (choice == move)
            {
                var target = await DisplayActionSheetAsync("Move entries to", "cancel", null, others.Select(c => c.Name).ToArray());
                var picked = others.FirstOrDefault(c => c.Name == target);
                if (picked == null) return;
                await _categories.DeleteAndReassignAsync(_category, picked.Id);
            }
            else if (choice != null && choice.StartsWith("Delete category", StringComparison.Ordinal))
            {
                if (!await DisplayAlertAsync("Delete permanently", $"{entries} entr{(entries == 1 ? "y" : "ies")} and their receipts will be deleted. This can't be undone.", "delete", "cancel")) return;
                await _categories.DeleteWithEntriesAsync(_category);
            }
            else return;
        }

        _reminders.CancelCategory(_category.Id);
        await Shell.Current.GoToAsync("..");
    }
}
