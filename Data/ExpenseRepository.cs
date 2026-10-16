using DayLogger.Models;
using SQLite;

namespace DayLogger.Data;

public class ExpenseRepository
{
    SQLiteAsyncConnection? _db;

    public static string ReceiptsDir => Path.Combine(FileSystem.AppDataDirectory, "receipts");
    public static string ReceiptPath(string fileName) => Path.Combine(ReceiptsDir, fileName);

    async Task<SQLiteAsyncConnection> Db()
    {
        if (_db != null) return _db;
        var db = new SQLiteAsyncConnection(Path.Combine(FileSystem.AppDataDirectory, "daylogger.db3"));
        await db.CreateTableAsync<Expense>();
        return _db = db;
    }

    public async Task<List<Expense>> GetDayAsync(DateOnly day)
    {
        var db = await Db();
        var key = MealRepository.Key(day);
        return await db.Table<Expense>().Where(e => e.Date == key).OrderBy(e => e.CreatedTicks).ToListAsync();
    }

    public async Task<List<Expense>> GetMonthAsync(int year, int month)
    {
        var db = await Db();
        var from = MealRepository.Key(new DateOnly(year, month, 1));
        var to = MealRepository.Key(new DateOnly(year, month, DateTime.DaysInMonth(year, month)));
        return await db.QueryAsync<Expense>("SELECT * FROM Expense WHERE Date >= ? AND Date <= ?", from, to);
    }

    public async Task SaveAsync(Expense expense)
    {
        var db = await Db();
        if (expense.Id == 0)
        {
            expense.CreatedTicks = DateTime.UtcNow.Ticks;
            await db.InsertAsync(expense);
        }
        else await db.UpdateAsync(expense);
    }

    public async Task DeleteAsync(Expense expense)
    {
        await (await Db()).DeleteAsync(expense);
        DeleteReceiptFile(expense.Receipt);
    }

    /// <summary>Copies a captured/picked photo into app storage and returns its file name.</summary>
    public static async Task<string> StoreReceiptAsync(FileResult photo)
    {
        Directory.CreateDirectory(ReceiptsDir);
        var ext = Path.GetExtension(photo.FileName);
        var name = $"{Guid.NewGuid():N}{(string.IsNullOrEmpty(ext) ? ".jpg" : ext)}";
        await using var src = await photo.OpenReadAsync();
        await using var dst = File.Create(ReceiptPath(name));
        await src.CopyToAsync(dst);
        return name;
    }

    public static void DeleteReceiptFile(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return;
        try { File.Delete(ReceiptPath(fileName)); } catch { /* already gone */ }
    }
}
