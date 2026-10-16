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

    // Same look as Metro.ListItem with the square avatar in front.
    static View Row(PersonSummary s, Func<Task> tapped)
    {
        var text = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        text.Add(new Label { Text = s.Person.Name.ToLowerInvariant(), Style = (Style)Application.Current!.Resources["ListPrimary"] });
        text.Add(new Label { Text = s.Text, Style = (Style)Application.Current!.Resources["Caption"] });

        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 14, Padding = new Thickness(20, 10) };
        row.Add(new Avatar { Name = s.Person.Name, Photo = s.Person.Photo, Size = 48 });
        row.Add(text, 1);
        row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await tapped()) });
        return row;
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
            List.Children.Add(Row(s, () => Shell.Current.GoToAsync($"person?id={id}")));
        }
        if (summaries.Count == 0) List.Children.Add(Metro.Note("people are also added while logging an entry: type who it was with"));
        await Scaffold.EnterAsync();
    }
}
