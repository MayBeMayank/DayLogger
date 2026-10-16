using System.Globalization;

namespace DayLogger.Models;

/// <summary>Formats amounts with the device's currency symbol (₹ for an India locale), symbol first.</summary>
public static class Money
{
    static string Symbol => CultureInfo.CurrentCulture.NumberFormat.CurrencySymbol;

    public static string Format(double v) => $"{Symbol}{v:0.##}";

    /// <summary>Short form for tight spaces, e.g. ₹1.5k.</summary>
    public static string Compact(double v) => v >= 1000 ? $"{Symbol}{v / 1000:0.#}k" : Format(v);
}
