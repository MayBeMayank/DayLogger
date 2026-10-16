using DayLogger.Controls;
using DayLogger.Data;
using DayLogger.Models;

namespace DayLogger.Pages;

public partial class StreakBinPage : ContentPage
{
    readonly StreakRepository _repo;

    public StreakBinPage(StreakRepository repo)
    {
        InitializeComponent();
        _repo = repo;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    async Task RefreshAsync()
    {
        var binned = await _repo.GetBinAsync();
        EmptyButton.IsVisible = binned.Count > 0;

        List.Children.Clear();
        if (binned.Count == 0)
        {
            List.Children.Add(new Label { Text = "the bin is empty", Style = (Style)Application.Current!.Resources["Caption"] });
            return;
        }
        foreach (var s in binned) List.Children.Add(BuildCard(s));
    }

    View BuildCard(Streak streak)
    {
        var info = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label { Text = streak.Name, FontAttributes = FontAttributes.Bold, FontSize = 16 },
                new Label
                {
                    Text = $"{streak.Frequency}x per day · deleted {new DateTime(streak.DeletedTicks!.Value, DateTimeKind.Utc).ToLocalTime():d MMM yyyy}",
                    Style = (Style)Application.Current!.Resources["Caption"],
                },
            },
        };

        var actions = new CardActions();
        var reveal = new Reveal { Body = actions };
        actions.Add("Restore", async () => { await _repo.RestoreAsync(streak); await RefreshAsync(); });
        actions.Add("Delete forever", async () => await DeleteForeverAsync(streak));

        var card = new Border { Padding = 12, Content = new VerticalStackLayout { Children = { info, reveal } } };
        var press = new LongPressBehavior();
        press.Pressed += (_, _) => Toggle(reveal);
        card.Behaviors.Add(press);
        return card;
    }

    Reveal? _openActions;

    // Only one card shows its actions at a time.
    void Toggle(Reveal reveal)
    {
        if (_openActions != null && _openActions != reveal) _openActions.IsOpen = false;
        reveal.IsOpen = !reveal.IsOpen;
        _openActions = reveal.IsOpen ? reveal : null;
    }

    async Task DeleteForeverAsync(Streak streak)
    {
        if (!await DisplayAlertAsync("Delete forever", $"{streak.Name} and all its history will be lost.", "delete", "keep")) return;
        await _repo.DeleteAsync(streak);
        await RefreshAsync();
    }

    async void OnEmptyClicked(object? sender, EventArgs e)
    {
        if (!await DisplayAlertAsync("Empty bin", "Everything in the bin and its history will be lost.", "empty", "keep")) return;
        await _repo.EmptyBinAsync();
        await RefreshAsync();
    }
}
