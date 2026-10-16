using System.Globalization;
using Microsoft.Maui.Graphics;
#if WINDOWS
using Microsoft.Maui.Graphics.Win2D;
#else
using Microsoft.Maui.Graphics.Platform;
#endif

namespace DayLogger.Services;

/// <summary>What a streak card shows for one month.</summary>
public record StreakCard(string Name, int Frequency, DateTime Month, IReadOnlyDictionary<int, double> FillByDay,
    int CompleteDays, int Current, int Best);

/// <summary>Draws share images straight to a bitmap, so the result doesn't depend on screen size or what's scrolled into view.</summary>
public static class ShareCardRenderer
{
    const float W = 1080, H = 1350, Pad = 80;

    static Color Res(string key) => (Color)Application.Current!.Resources[key];

    /// <summary>PNG bytes of a month heatmap card, in the app's current theme colours.</summary>
    public static byte[] RenderStreak(StreakCard card)
    {
        Color paper = Res("Paper"), surface = Res("Surface"), ink = Res("Ink"), muted = Res("Muted"), primary = Res("Primary"), onPrimary = Res("OnPrimary");

        using var ctx = CreateContext();
        var c = ctx.Canvas;

        c.FillColor = paper;
        c.FillRectangle(0, 0, W, H);

        Text(c, card.Name, Pad, 80, W - 2 * Pad, 90, 66, ink, bold: true);
        var month = card.Month.ToString("MMMM yyyy", CultureInfo.CurrentCulture).ToUpperInvariant();
        Text(c, $"{month}  ·  {card.Frequency}x per day", Pad, 175, W - 2 * Pad, 50, 32, muted);

        // Weekday header and month grid, Monday first like the in-app calendar.
        const float gap = 10;
        float cell = (W - 2 * Pad - 6 * gap) / 7;
        float top = 270;
        for (int i = 0; i < 7; i++)
        {
            var name = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)((1 + i) % 7));
            Text(c, name[..Math.Min(2, name.Length)].ToUpperInvariant(), Pad + i * (cell + gap), top, cell, 40, 26, muted, center: true);
        }
        top += 50;

        int offset = ((int)new DateTime(card.Month.Year, card.Month.Month, 1).DayOfWeek + 6) % 7;
        int days = DateTime.DaysInMonth(card.Month.Year, card.Month.Month);
        for (int day = 1; day <= days; day++)
        {
            int slot = offset + day - 1;
            float x = Pad + slot % 7 * (cell + gap), y = top + slot / 7 * (cell + gap);
            card.FillByDay.TryGetValue(day, out var fill);

            c.FillColor = surface;
            c.FillRoundedRectangle(x, y, cell, cell, 24);
            if (fill > 0)
            {
                c.FillColor = primary.WithAlpha((float)(0.3 + 0.7 * Math.Clamp(fill, 0, 1)));
                c.FillRoundedRectangle(x, y, cell, cell, 24);
            }
            Text(c, day.ToString(), x, y, cell, cell, 34, fill >= 1 ? onPrimary : ink, center: true, bold: fill >= 1);
        }

        float footer = 1150;
        Text(c, $"{card.CompleteDays} days completed", Pad, footer, W - 2 * Pad, 56, 40, ink, bold: true);
        Text(c, $"current streak {card.Current}  ·  best {card.Best}", Pad, footer + 62, W - 2 * Pad, 44, 30, muted);
        Text(c, "DayLogger", Pad, H - 90, W - 2 * Pad, 40, 26, primary, bold: true);

        return ctx.Image.AsBytes(ImageFormat.Png);
    }

    static BitmapExportContext CreateContext() =>
#if WINDOWS
        new W2DBitmapExportService().CreateContext((int)W, (int)H, 1);
#else
        new PlatformBitmapExportContext((int)W, (int)H, 1);
#endif

    static void Text(ICanvas c, string text, float x, float y, float w, float h, float size, Color color, bool center = false, bool bold = false)
    {
        c.Font = bold ? Microsoft.Maui.Graphics.Font.DefaultBold : Microsoft.Maui.Graphics.Font.Default;
        c.FontSize = size;
        c.FontColor = color;
        c.DrawString(text, x, y, w, h, center ? HorizontalAlignment.Center : HorizontalAlignment.Left, VerticalAlignment.Center);
    }
}
