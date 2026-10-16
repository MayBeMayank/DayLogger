using DayLogger.Models;
using SQLite;

namespace DayLogger.Data;

public class MealRepository
{
    const string DateFormat = "yyyy-MM-dd";
    SQLiteAsyncConnection? _db;

    public static string Key(DateOnly d) => d.ToString(DateFormat);

    async Task<SQLiteAsyncConnection> Db()
    {
        if (_db != null) return _db;
        var db = new SQLiteAsyncConnection(Path.Combine(FileSystem.AppDataDirectory, "daylogger.db3"));
        await db.CreateTableAsync<Meal>();
        return _db = db;
    }

    public async Task<List<Meal>> GetDayAsync(DateOnly day)
    {
        var db = await Db();
        var key = Key(day);
        return await db.Table<Meal>().Where(m => m.Date == key).OrderBy(m => m.Type).ThenBy(m => m.CreatedTicks).ToListAsync();
    }

    public async Task<List<Meal>> GetMonthAsync(int year, int month)
    {
        var db = await Db();
        var from = Key(new DateOnly(year, month, 1));
        var to = Key(new DateOnly(year, month, DateTime.DaysInMonth(year, month)));
        // sqlite-net can't translate string.CompareTo, so use raw SQL (dates are yyyy-MM-dd text).
        return await db.QueryAsync<Meal>("SELECT * FROM Meal WHERE Date >= ? AND Date <= ?", from, to);
    }

    public async Task SaveAsync(Meal meal)
    {
        var db = await Db();
        if (meal.Id == 0)
        {
            meal.CreatedTicks = DateTime.UtcNow.Ticks;
            await db.InsertAsync(meal);
        }
        else await db.UpdateAsync(meal);
    }

    public async Task DeleteAsync(Meal meal) => await (await Db()).DeleteAsync(meal);
}
