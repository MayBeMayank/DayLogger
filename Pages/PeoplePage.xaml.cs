using DayLogger.Controls;
using DayLogger.Data;
using DayLogger.Models;

namespace DayLogger.Pages;

/// <summary>Everyone entries are linked to, with their totals across categories; a person opens their own page.</summary>
public partial class PeoplePage : ContentPage
{
    readonly PersonRepository _people;

    public PeoplePage(PersonRepository people)
    {
        InitializeComponent();
        _people = people;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var summaries = await _people.GetSummariesAsync();
        var open = summaries.Sum(s => s.Open);
        Scaffold.Subtitle = open > 0 ? $"{Money.Format(open)} yet to come back" : "shops, vendors and friends you deal with";

        List.Children.Clear();
        List.Children.Add(Metro.ListItem("new person", "add someone to link entries to", () => Shell.Current.GoToAsync("person?id=0")));
        foreach (var s in summaries)
        {
            var id = s.Person.Id;
            List.Children.Add(Metro.ListItem(s.Person.Name.ToLowerInvariant(), s.Text, () => Shell.Current.GoToAsync($"person?id={id}")));
        }
        if (summaries.Count == 0) List.Children.Add(Metro.Note("people are also added while logging an entry: type who it was with"));
    }
}
