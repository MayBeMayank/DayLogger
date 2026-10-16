using DayLogger.Data;

namespace DayLogger.Controls;

/// <summary>
/// Square profile picture. Shows the person's photo when there is one, else the first letter of the name on a flat tint,
/// else a person glyph (nothing typed yet).
/// </summary>
public class Avatar : ContentView
{
    public static readonly BindableProperty NameProperty =
        BindableProperty.Create(nameof(Name), typeof(string), typeof(Avatar), "", propertyChanged: (b, _, _) => ((Avatar)b).Update());

    /// <summary>File name inside the people photo folder (not a path).</summary>
    public static readonly BindableProperty PhotoProperty =
        BindableProperty.Create(nameof(Photo), typeof(string), typeof(Avatar), null, propertyChanged: (b, _, _) => ((Avatar)b).Update());

    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(nameof(Size), typeof(double), typeof(Avatar), 40.0, propertyChanged: (b, _, _) => ((Avatar)b).Update());

    public string? Name { get => (string?)GetValue(NameProperty); set => SetValue(NameProperty, value); }
    public string? Photo { get => (string?)GetValue(PhotoProperty); set => SetValue(PhotoProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    readonly Label _initial = new() { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
    readonly Image _image = new() { Aspect = Aspect.AspectFill, IsVisible = false };

    public Avatar()
    {
        _initial.SetDynamicResource(Label.TextColorProperty, "Ink");
        var box = new Border { StrokeThickness = 0, Content = new Grid { Children = { _initial, _image } } };
        box.SetDynamicResource(VisualElement.BackgroundColorProperty, "PrimaryFill");
        Content = box;
        HorizontalOptions = VerticalOptions = LayoutOptions.Center;
        Update();
    }

    void Update()
    {
        WidthRequest = HeightRequest = Size;
        _initial.FontSize = Size * 0.45;

        var name = Name?.Trim() ?? "";
        _initial.Text = name.Length == 0 ? "\U0001F464" : new System.Globalization.StringInfo(name).SubstringByTextElements(0, 1).ToUpperInvariant();

        var file = string.IsNullOrEmpty(Photo) ? null : PersonRepository.PhotoPath(Photo);
        bool has = file != null && File.Exists(file);
        _image.Source = has ? ImageSource.FromFile(file!) : null;
        _image.IsVisible = has;
    }
}
