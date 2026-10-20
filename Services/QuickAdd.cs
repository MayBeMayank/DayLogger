namespace DayLogger.Services;

/// <summary>
/// Hand-off from outside the app (home-screen widget) to the home page. A cold start has no page to navigate yet, so the
/// request waits until the home page takes it; a running app gets it through <see cref="Requested"/>.
/// </summary>
public static class QuickAdd
{
    public const string ExpenseUri = "daylogger://add/expense";

    static bool _pending;

    public static event Action? Requested;

    public static void Request()
    {
        _pending = true;
        Requested?.Invoke();
    }

    public static bool Take()
    {
        var was = _pending;
        _pending = false;
        return was;
    }
}
