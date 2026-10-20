using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using DayLogger.Controls;
using DayLogger.Data;
using DayLogger.Models;
using DayLogger.Services;
using Microsoft.Maui.Platform;
using System.Globalization;
using AColor = Android.Graphics.Color;

namespace DayLogger;

/// <summary>
/// A small sheet over the home screen for logging an expense: category, subcategory, amount, title. Widgets cannot hold text
/// boxes, so the widget opens this. Anything richer (person, tags, receipt) is one tap away in the full form ("more").
/// Drawn natively with the app's palette so it opens fast without starting the MAUI page stack.
/// </summary>
[Activity(Theme = "@style/QuickEntryTheme", Exported = false, ExcludeFromRecents = true, LaunchMode = LaunchMode.SingleTask,
    WindowSoftInputMode = SoftInput.AdjustResize | SoftInput.StateHidden)]
public class QuickEntryActivity : Activity
{
    const string LastCategoryPref = "expenses.lastCat";   // shared with the day page

    LinearLayout _card = null!;
    LinearLayout _subRow = null!;
    HorizontalScrollView _subScroll = null!;
    EditText _amount = null!, _title = null!;
    readonly List<TextView> _catChips = new(), _subChips = new();
    List<ExpenseCategory> _cats = new();
    List<ExpenseSubcategory> _subs = new();
    int _cat = -1, _sub = -1;

    CategoryRepository Categories => IPlatformApplication.Current!.Services.GetRequiredService<CategoryRepository>();
    ExpenseRepository Expenses => IPlatformApplication.Current!.Services.GetRequiredService<ExpenseRepository>();

    static AColor Colour(string key) => Metro.Colour(key).ToPlatform();
    int Dp(double v) => (int)(v * Resources!.DisplayMetrics!.Density + 0.5);

    protected override async void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var root = new FrameLayout(this);
        root.SetBackgroundColor(AColor.Argb(120, 0, 0, 0));
        root.Click += (_, _) => Finish();
        // Edge-to-edge: lift the sheet above the keyboard and the navigation bar.
        root.SetOnApplyWindowInsetsListener(new InsetsPadding());

        _card = new LinearLayout(this) { Orientation = Orientation.Vertical, Clickable = true };
        _card.SetBackgroundColor(Colour("Paper"));
        _card.SetPadding(Dp(16), Dp(16), Dp(16), Dp(16));
        root.AddView(_card, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom));
        SetContentView(root);

        try { await BuildAsync(); }
        catch (Exception ex) { Toast.MakeText(this, ex.Message, ToastLength.Long)?.Show(); Finish(); }
    }

    async Task BuildAsync()
    {
        _cats = await Categories.GetAllAsync();
        _card.AddView(Text("NEW ENTRY · TODAY", 11, "Muted"));

        if (_cats.Count == 0)
        {
            _card.AddView(Text("Create a category first (Settings → manage categories).", 15, "Ink", top: 10));
            _card.AddView(Button("OPEN DAYLOGGER", false, OpenApp), Lp(top: 12));
            return;
        }

        var catScroll = Scroller(out var catRow);
        for (int i = 0; i < _cats.Count; i++)
        {
            int at = i;
            var chip = Chip(_cats[i].Name.ToLowerInvariant(), () => _ = SelectCategoryAsync(at));
            _catChips.Add(chip);
            catRow.AddView(chip);
        }
        _card.AddView(catScroll, Lp(top: 10));

        _subScroll = Scroller(out _subRow);
        _card.AddView(_subScroll, Lp(top: 4));

        var fields = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _amount = Field("amount", numeric: true);
        _title = Field("what for", numeric: false);
        fields.AddView(_amount, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { RightMargin = Dp(8) });
        fields.AddView(_title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 2f));
        _card.AddView(fields, Lp(top: 12));

        var buttons = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        buttons.AddView(Button("MORE", false, OpenApp), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { RightMargin = Dp(8) });
        buttons.AddView(Button("SAVE", true, () => _ = SaveAsync()), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        _card.AddView(buttons, Lp(top: 12));

        var last = Preferences.Get(LastCategoryPref, 0);
        await SelectCategoryAsync(Math.Max(0, _cats.FindIndex(c => c.Id == last)));
        _amount.RequestFocus();
        ((InputMethodManager)GetSystemService(InputMethodService)!).ShowSoftInput(_amount, ShowFlags.Implicit);
    }

    async Task SelectCategoryAsync(int at)
    {
        _cat = at;
        Style(_catChips, at);
        _subs = await Categories.GetSubcategoriesAsync(_cats[at].Id);
        _subChips.Clear();
        _subRow.RemoveAllViews();
        for (int i = 0; i < _subs.Count; i++)
        {
            int s = i;
            var chip = Chip(_subs[i].Name.ToLowerInvariant(), () => { _sub = _sub == s ? -1 : s; Style(_subChips, _sub); });
            _subChips.Add(chip);
            _subRow.AddView(chip);
        }
        _subScroll.Visibility = _subs.Count > 0 ? ViewStates.Visible : ViewStates.Gone;
        _sub = _subs.FindIndex(s => s.Id == Preferences.Get($"expenses.lastSub.{_cats[at].Id}", 0));
        Style(_subChips, _sub);
    }

    async Task SaveAsync()
    {
        var category = _cats[_cat];
        double amount = 0;
        bool blank = string.IsNullOrWhiteSpace(_amount.Text);
        bool valid = blank
            ? !category.AmountRequired
            : double.TryParse(_amount.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out amount) && amount >= 0 && (amount > 0 || !category.AmountRequired);
        if (!valid) { _amount.RequestFocus(); return; }

        // The full form insists on a title; here a blank one falls back to what was picked.
        int? subId = _sub >= 0 ? _subs[_sub].Id : null;
        var title = _title.Text?.Trim();
        if (string.IsNullOrEmpty(title)) title = _sub >= 0 ? _subs[_sub].Name : category.Name;

        await Expenses.SaveAsync(new Expense
        {
            Date = DateKey.Of(DateOnly.FromDateTime(DateTime.Today)),
            CategoryId = category.Id,
            SubcategoryId = subId,
            Title = title,
            Amount = amount,
        });
        Preferences.Set(LastCategoryPref, category.Id);
        Preferences.Set($"expenses.lastSub.{category.Id}", subId ?? 0);
        Toast.MakeText(this, $"logged · {category.Name.ToLowerInvariant()}{(amount > 0 ? " · " + Money.Format(amount) : "")}", ToastLength.Short)?.Show();
        Finish();
    }

    void OpenApp()
    {
        var intent = new Intent(this, typeof(MainActivity)).SetAction(Intent.ActionView)!
            .SetData(Android.Net.Uri.Parse(QuickAdd.ExpenseUri))!
            .AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
        StartActivity(intent);
        Finish();
    }

    // ---- small view helpers (flat, square, palette from ThemeService) ----

    LinearLayout.LayoutParams Lp(int top) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = Dp(top) };

    TextView Text(string text, float size, string colour, int top = 0)
    {
        var t = new TextView(this) { Text = text, TextSize = size };
        t.SetTextColor(Colour(colour));
        if (top > 0) t.SetPadding(0, Dp(top), 0, 0);
        return t;
    }

    HorizontalScrollView Scroller(out LinearLayout row)
    {
        var scroll = new HorizontalScrollView(this) { HorizontalScrollBarEnabled = false };
        row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        scroll.AddView(row);
        return scroll;
    }

    TextView Chip(string text, Action tapped)
    {
        var chip = new TextView(this) { Text = text, TextSize = 14 };
        chip.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
        chip.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { RightMargin = Dp(6), BottomMargin = Dp(2) };
        chip.Click += (_, _) => tapped();
        return chip;
    }

    GradientDrawable Box(string fill, string stroke, double width)
    {
        var bg = new GradientDrawable();
        bg.SetColor(Colour(fill));
        bg.SetStroke(Dp(width), Colour(stroke));
        return bg;
    }

    // Selected: soft primary fill with a primary outline, like ChipGroup; text stays Ink in both states.
    void Style(List<TextView> chips, int selected)
    {
        for (int i = 0; i < chips.Count; i++)
        {
            bool on = i == selected;
            chips[i].Background = Box(on ? "PrimaryFill" : "Paper", on ? "Primary" : "Line", on ? 1.5 : 1);
            chips[i].SetTextColor(Colour("Ink"));
            chips[i].SetTypeface(null, on ? TypefaceStyle.Bold : TypefaceStyle.Normal);
        }
    }

    EditText Field(string hint, bool numeric)
    {
        var e = new EditText(this) { Hint = hint, TextSize = 16 };
        e.SetTextColor(Colour("Ink"));
        e.SetHintTextColor(Colour("Muted"));
        e.SetSingleLine(true);
        e.InputType = numeric ? InputTypes.ClassNumber | InputTypes.NumberFlagDecimal : InputTypes.ClassText | InputTypes.TextFlagCapSentences;
        e.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
        e.Background = Box("Surface", "Line", 1);
        return e;
    }

    TextView Button(string text, bool primary, Action tapped)
    {
        var b = new TextView(this) { Text = text, TextSize = 14, Gravity = GravityFlags.Center };
        b.SetTypeface(null, TypefaceStyle.Bold);
        b.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
        b.SetTextColor(Colour(primary ? "OnPrimary" : "Ink"));
        b.Background = Box(primary ? "Primary" : "Paper", primary ? "Primary" : "Line", 1);
        b.Click += (_, _) => tapped();
        return b;
    }

    sealed class InsetsPadding : Java.Lang.Object, Android.Views.View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(Android.Views.View? v, WindowInsets? insets)
        {
#pragma warning disable CA1422 // the platform getters are what works on every API level
            v?.SetPadding(0, 0, 0, insets?.SystemWindowInsetBottom ?? 0);
#pragma warning restore CA1422
            return insets!;
        }
    }
}
