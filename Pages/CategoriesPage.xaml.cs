using DayLogger.Controls;
using DayLogger.Data;

namespace DayLogger.Pages;

/// <summary>Where categories are managed (Settings): add one, or open one to edit, reorder, archive or delete it.</summary>
public partial class CategoriesPage : ContentPage
{
    readonly CategoryRepository _categories;

    public CategoriesPage(CategoryRepository categories)
    {
        InitializeComponent();
        _categories = categories;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var all = await _categories.GetAllAsync(includeArchived: true);

        List.Children.Clear();
        List.Children.Add(Metro.ListItem("new category", "Meals, Travel, Bills…", () => Shell.Current.GoToAsync("category?id=0")));
        foreach (var c in all)
        {
            var id = c.Id;
            int entries = await _categories.CountEntriesAsync(id);
            var detail = $"{entries} entr{(entries == 1 ? "y" : "ies")}" + (c.Archived ? " · archived" : "");
            List.Children.Add(Metro.ListItem(c.Name.ToLowerInvariant(), detail, () => Shell.Current.GoToAsync($"category?id={id}")));
        }
        if (all.Count > 0) List.Children.Add(Metro.Note("open a category to rename it, change its subcategories, reorder, archive or delete"));
        await Scaffold.EnterAsync();
    }
}
