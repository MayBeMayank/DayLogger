namespace DayLogger.Controls;

/// <summary>Single-select row of bordered text chips.</summary>
public class ChipGroup : ContentView
{
    readonly FlexLayout _row = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
    readonly List<(Border Chip, Label Text)> _chips = new();
    int _selected = -1;

    public event EventHandler? SelectionChanged;

    public ChipGroup() => Content = _row;

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            _selected = value;
            for (int i = 0; i < _chips.Count; i++)
            {
                bool on = i == value;
                // Text stays Ink on both states so it can never match the fill; selection is a soft primary tint + outline.
                _chips[i].Chip.SetDynamicResource(VisualElement.BackgroundColorProperty, on ? "PrimaryFill" : "Paper");
                _chips[i].Chip.SetDynamicResource(Border.StrokeProperty, on ? "Primary" : "Line");
                _chips[i].Chip.StrokeThickness = on ? 1.5 : 1;
                _chips[i].Text.FontAttributes = on ? FontAttributes.Bold : FontAttributes.None;
                _chips[i].Text.SetDynamicResource(Label.TextColorProperty, "Ink");
            }
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetItems(IEnumerable<string> items)
    {
        _row.Children.Clear();
        _chips.Clear();
        int i = 0;
        foreach (var item in items)
        {
            int index = i++;
            var label = new Label { Text = item, FontSize = 13, Padding = new Thickness(10, 7) };
            var chip = new Border { Content = label, Margin = new Thickness(0, 0, 6, 6), Style = (Style)Application.Current!.Resources["ControlBox"] };
            chip.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => SelectedIndex = index) });
            _chips.Add((chip, label));
            _row.Children.Add(chip);
        }
    }
}
