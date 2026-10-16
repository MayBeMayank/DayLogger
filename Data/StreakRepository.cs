using DayLogger.Models;
using SQLite;

namespace DayLogger.Data;

public class StreakRepository
{
    SQLiteAsyncConnection? _db;

    async Task<SQLiteAsyncConnection> Db()
    {
        if (_db != null) return _db;
        var db = new SQLiteAsyncConnection(Path.Combine(FileSystem.AppDataDirectory, "daylogger.db3"));
        await db.CreateTableAsync<Streak>();
        await db.CreateTableAsync<StreakLog>();
        return _db = db;
    }

    public async Task<List<Streak>> GetAllAsync() =>
        await (await Db()).QueryAsync<Streak>("SELECT * FROM Streak WHERE DeletedTicks IS NULL ORDER BY CreatedTicks");

    /// <summary>Binned streaks, most recently binned first.</summary>
    public async Task<List<Streak>> GetBinAsync() =>
        await (await Db()).QueryAsync<Streak>("SELECT * FROM Streak WHERE DeletedTicks IS NOT NULL ORDER BY DeletedTicks DESC");

    public async Task<int> BinCountAsync() =>
        await (await Db()).ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Streak WHERE DeletedTicks IS NOT NULL");

    public async Task MoveToBinAsync(Streak streak)
    {
        streak.DeletedTicks = DateTime.UtcNow.Ticks;
        await (await Db()).UpdateAsync(streak);
    }

    public async Task RestoreAsync(Streak streak)
    {
        streak.DeletedTicks = null;
        await (await Db()).UpdateAsync(streak);
    }

    public async Task EmptyBinAsync()
    {
        foreach (var s in await GetBinAsync()) await DeleteAsync(s);
    }

    public async Task<Streak?> GetAsync(int id) =>
        await (await Db()).Table<Streak>().Where(s => s.Id == id).FirstOrDefaultAsync();

    public async Task SaveAsync(Streak streak)
    {
        var db = await Db();
        if (streak.Id == 0)
        {
            streak.CreatedTicks = DateTime.UtcNow.Ticks;
            await db.InsertAsync(streak);
        }
        else await db.UpdateAsync(streak);
    }

    /// <summary>Permanent: removes the streak and all its history.</summary>
    public async Task DeleteAsync(Streak streak)
    {
        var db = await Db();
        await db.ExecuteAsync("DELETE FROM StreakLog WHERE StreakId = ?", streak.Id);
        await db.DeleteAsync(streak);
    }

    /// <summary>Ticked box indices per streak id for one day.</summary>
    public async Task<Dictionary<int, HashSet<int>>> GetDayChecksAsync(DateOnly day)
    {
        var db = await Db();
        var rows = await db.QueryAsync<StreakLog>("SELECT * FROM StreakLog WHERE Date = ?", MealRepository.Key(day));
        return rows.ToDictionary(r => r.StreakId, r => r.Indices);
    }

    /// <summary>All check rows of one month, for every streak.</summary>
    public async Task<List<StreakLog>> GetMonthLogsAsync(int year, int month)
    {
        var db = await Db();
        var from = MealRepository.Key(new DateOnly(year, month, 1));
        var to = MealRepository.Key(new DateOnly(year, month, DateTime.DaysInMonth(year, month)));
        return await db.QueryAsync<StreakLog>("SELECT * FROM StreakLog WHERE Date >= ? AND Date <= ?", from, to);
    }

    /// <summary>Every logged day of a streak (ticked box indices), keyed by yyyy-MM-dd.</summary>
    public async Task<Dictionary<string, HashSet<int>>> GetChecksAsync(int streakId)
    {
        var db = await Db();
        var rows = await db.QueryAsync<StreakLog>("SELECT * FROM StreakLog WHERE StreakId = ?", streakId);
        return rows.ToDictionary(r => r.Date, r => r.Indices);
    }

    public async Task SetChecksAsync(int streakId, DateOnly day, IReadOnlyCollection<int> ticked)
    {
        var db = await Db();
        var key = MealRepository.Key(day);
        var existing = (await db.QueryAsync<StreakLog>("SELECT * FROM StreakLog WHERE StreakId = ? AND Date = ?", streakId, key)).FirstOrDefault();
        if (ticked.Count == 0)
        {
            if (existing != null) await db.DeleteAsync(existing);
            return;
        }
        var row = existing ?? new StreakLog { StreakId = streakId, Date = key };
        row.Indices = ticked.ToHashSet();
        if (existing != null) await db.UpdateAsync(row); else await db.InsertAsync(row);
    }
}
