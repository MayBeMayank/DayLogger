using DayLogger.Models;
using SQLite;

namespace DayLogger.Data;

/// <summary>"yyyy-MM-dd" keys, which sort and range-query as plain text.</summary>
public static class DateKey
{
    const string Format = "yyyy-MM-dd";

    public static string Of(DateOnly d) => d.ToString(Format);
}

/// <summary>
/// The shared connection for expenses and their categories, created once. The first open also does the one-time
/// clean-up from the old Meal Tracker / flat Expense schema.
/// </summary>
public static class AppDatabase
{
    public const string DefaultCategoryName = "Expenses";

    static readonly SemaphoreSlim Gate = new(1, 1);
    static SQLiteAsyncConnection? _db;

    public static async Task<SQLiteAsyncConnection> OpenAsync()
    {
        if (_db != null) return _db;
        await Gate.WaitAsync();
        try
        {
            if (_db != null) return _db;
            var db = new SQLiteAsyncConnection(Path.Combine(FileSystem.AppDataDirectory, "daylogger.db3"));
            await db.CreateTableAsync<ExpenseCategory>();
            await db.CreateTableAsync<ExpenseSubcategory>();
            await db.CreateTableAsync<Expense>();
            await db.CreateTableAsync<ExpenseTag>();

            // Meal Tracker is gone: meals are now an ordinary category the user creates.
            await db.ExecuteAsync("DROP TABLE IF EXISTS Meal");

            // Expenses saved before categories existed move into one default category.
            var orphans = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Expense WHERE CategoryId IS NULL OR CategoryId = 0");
            if (orphans > 0)
            {
                var category = new ExpenseCategory { Name = DefaultCategoryName, CreatedTicks = DateTime.UtcNow.Ticks };
                await db.InsertAsync(category);
                await db.ExecuteAsync("UPDATE Expense SET CategoryId = ? WHERE CategoryId IS NULL OR CategoryId = 0", category.Id);
            }
            return _db = db;
        }
        finally { Gate.Release(); }
    }
}
