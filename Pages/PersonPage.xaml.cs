using System.Globalization;
using DayLogger.Controls;
using DayLogger.Data;
using DayLogger.Models;

namespace DayLogger.Pages;

/// <summary>Edits one person (id 0 creates one) and lists every entry made with them, across categories.</summary>
[QueryProperty(nameof(IdText), "id")]
public partial class PersonPage : ContentPage
{
    readonly PersonRepository _people;
    readonly ExpenseRepository _expenses;
    readonly CategoryRepository _categories;
    Person? _person;
    int _id;
    bool _newShown;

    public string IdText { set => _id = int.TryParse(value, out var id) ? id : 0; }

    public PersonPage(PersonRepository people, ExpenseRepository expenses, CategoryRepository categories)
    {
        InitializeComponent();
        _people = people;
        _expenses = expenses;
        _categories = categories;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_id == 0)
        {
            if (_newShown) return;
            _newShown = true;
            Scaffold.Title = "new person";
            ExistingOnly.IsVisible = false;
            NameEntry.FocusSoon();
            return;
        }

        // Fields are filled once; coming back from an entry page only refreshes the list.
        if (_person == null)
        {
            _person = await _people.GetAsync(_id);
            if (_person == null) { await Shell.Current.GoToAsync(".."); return; }
            Scaffold.Title = _person.Name;
            NameEntry.Text = _person.Name;
            PhoneEntry.Text = _person.Phone;
            NoteEntry.Text = _person.Note;
        }
        ContactButtons.IsVisible = !string.IsNullOrWhiteSpace(_person.Phone);
        await ShowEntriesAsync();
    }

    async Task ShowEntriesAsync()
    {
        var rows = await _expenses.GetByPersonAsync(_id);
        var names = (await _categories.GetAllAsync(includeArchived: true)).ToDictionary(c => c.Id, c => c.Name.ToLowerInvariant());
        Scaffold.Subtitle = PersonSummary.Of(_person!, rows).Text;
        SummaryLabel.Text = rows.Count == 0 ? "nothing logged with this person yet" : "";

        Entries.Children.Clear();
        foreach (var e in rows)
        {
            var row = e;
            var day = DateOnly.ParseExact(e.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var amount = e.Amount > 0 ? "  " + e.AmountText : "";
            var settle = e.HasSettle ? " · " + e.SettleText : "";
            Entries.Children.Add(Metro.ListItem(e.Title.ToLowerInvariant() + amount,
                $"{day.ToString("d MMM yyyy", CultureInfo.CurrentCulture)} · {names.GetValueOrDefault(e.CategoryId, "")}{settle}",
                () => Shell.Current.GoToAsync($"expenseday?cat={row.CategoryId}&date={row.Date}")));
        }
    }

    async void OnSaveClicked(object? sender, EventArgs e)
    {
        var name = NameEntry.Text?.Trim();
        if (string.IsNullOrEmpty(name)) { NameEntry.Focus(); return; }

        var person = _person ?? new Person();
        person.Name = name;
        person.Phone = string.IsNullOrWhiteSpace(PhoneEntry.Text) ? null : PhoneEntry.Text.Trim();
        person.Note = string.IsNullOrWhiteSpace(NoteEntry.Text) ? null : NoteEntry.Text.Trim();
        await _people.SaveAsync(person);

        if (_id == 0) { await Shell.Current.GoToAsync(".."); return; }
        Scaffold.Title = person.Name;
        ContactButtons.IsVisible = person.Phone != null;
    }

    async void OnCallClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_person?.Phone)) return;
        try { PhoneDialer.Default.Open(_person.Phone); }
        catch (FeatureNotSupportedException) { await DisplayAlertAsync("Call", "Calling is not available on this device.", "ok"); }
    }

    async void OnMessageClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_person?.Phone)) return;
        try { await Sms.Default.ComposeAsync(new SmsMessage("", _person.Phone)); }
        catch (FeatureNotSupportedException) { await DisplayAlertAsync("Message", "Messaging is not available on this device.", "ok"); }
    }

    async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (_person == null) return;
        if (!await DisplayAlertAsync("Delete person", $"{_person.Name}. Their entries are kept, just without a person.", "delete", "keep")) return;
        await _people.DeleteAsync(_person);
        await Shell.Current.GoToAsync("..");
    }
}
