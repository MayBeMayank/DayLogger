using DayLogger.Models;
using SQLite;

namespace DayLogger.Data;

/// <summary>How often a tag has been used (for suggestions).</summary>
public class TagUse
{
    public string Tag { get; set; } = "";
    public int Uses { get; set; }
}

public class ExpenseRepository
{
    public static string ReceiptsDir => Path.Combine(FileSystem.AppDataDirectory, "receipts");
    public static string ReceiptPath(string fileName) => Path.Combine(ReceiptsDir, fileName);

    public static string NormalizeTag(string tag) => tag.Trim().TrimStart('#').Trim().ToLowerInvariant();

    /// <summary>Splits free text ("home, quick  #lunch") into distinct normalized tags.</summary>
    public static List<string> ParseTags(string? text) =>
        (text ?? "").Split(new[] { ',', ';', ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeTag).Where(t => t.Length > 0).Distinct().ToList();

    public async Task<List<Expense>> GetDayAsync(int categoryId, DateOnly day)
    {
        var db = await AppDatabase.OpenAsync();
        var key = DateKey.Of(day);
        var rows = await db.Table<Expense>().Where(e => e.CategoryId == categoryId && e.Date == key).OrderBy(e => e.CreatedTicks).ToListAsync();
        await HydrateAsync(db, rows);
        return rows;
    }

    public async Task<List<Expense>> GetMonthAsync(int categoryId, int year, int month)
    {
        var db = await AppDatabase.OpenAsync();
        var from = DateKey.Of(new DateOnly(year, month, 1));
        var to = DateKey.Of(new DateOnly(year, month, DateTime.DaysInMonth(year, month)));
        // sqlite-net can't translate string.CompareTo, so use raw SQL (dates are yyyy-MM-dd text).
        var rows = await db.QueryAsync<Expense>("SELECT * FROM Expense WHERE CategoryId = ? AND Date >= ? AND Date <= ?", categoryId, from, to);
        await HydrateAsync(db, rows);
        return rows;
    }

    /// <summary>Every entry made with one person, newest first, across categories (null = everyone's, for the people list).</summary>
    public async Task<List<Expense>> GetByPersonAsync(int? personId)
    {
        var db = await AppDatabase.OpenAsync();
        var rows = personId is { } id
            ? await db.QueryAsync<Expense>("SELECT * FROM Expense WHERE PersonId = ? ORDER BY Date DESC, CreatedTicks DESC", id)
            : await db.QueryAsync<Expense>("SELECT * FROM Expense WHERE PersonId IS NOT NULL");
        await HydrateAsync(db, rows);
        return rows;
    }

    static async Task HydrateAsync(SQLiteAsyncConnection db, List<Expense> rows)
    {
        if (rows.Count == 0) return;
        var ids = string.Join(",", rows.Select(r => r.Id));
        var tags = await db.QueryAsync<ExpenseTag>($"SELECT * FROM ExpenseTag WHERE ExpenseId IN ({ids}) ORDER BY Id");
        var subs = (await db.Table<ExpenseSubcategory>().ToListAsync()).ToDictionary(s => s.Id, s => s.Name);
        var people = (await db.Table<Person>().ToListAsync()).ToDictionary(p => p.Id);
        foreach (var r in rows)
        {
            r.Tags = tags.Where(t => t.ExpenseId == r.Id).Select(t => t.Tag).ToList();
            if (r.SubcategoryId is { } sid && subs.TryGetValue(sid, out var name)) r.SubcategoryName = name;
            if (r.PersonId is { } pid && people.TryGetValue(pid, out var who)) { r.PersonName = who.Name; r.PersonPhoto = who.Photo; }
        }

        // Paybacks may sit in another month than the spend they settle, so they are looked up by link, not by the rows' range.
        var spends = rows.Where(r => !r.IsPayback).ToList();
        if (spends.Count > 0)
        {
            var keys = string.Join(",", spends.Select(r => r.CreatedTicks));
            var backs = await db.QueryAsync<Expense>($"SELECT * FROM Expense WHERE IsPayback = 1 AND SettlesTicks IN ({keys}) ORDER BY Date, CreatedTicks");
            foreach (var s in spends) s.Paybacks = backs.Where(b => b.SettlesTicks == s.CreatedTicks).ToList();
        }

        var linked = rows.Where(r => r.IsPayback && r.SettlesTicks != null).ToList();
        if (linked.Count > 0)
        {
            var keys = string.Join(",", linked.Select(r => r.SettlesTicks));
            var targets = await db.QueryAsync<Expense>($"SELECT * FROM Expense WHERE IsPayback = 0 AND CreatedTicks IN ({keys})");
            foreach (var p in linked) p.LinkedTitle = targets.FirstOrDefault(t => t.CreatedTicks == p.SettlesTicks)?.Title;
        }
    }

    /// <summary>
    /// Spends of a category a payback could settle: not fully returned yet, open ones first, then newest. <paramref name="include"/>
    /// keeps an already-linked or just-chosen spend in the list even when it is old or fully returned.
    /// </summary>
    public async Task<List<Expense>> GetLinkCandidatesAsync(int categoryId, long? include = null)
    {
        var db = await AppDatabase.OpenAsync();
        var rows = await db.QueryAsync<Expense>("SELECT * FROM Expense WHERE CategoryId = ? AND IsPayback = 0 AND Amount > 0 ORDER BY Date DESC, CreatedTicks DESC LIMIT 40", categoryId);
        if (include is { } t && rows.All(r => r.CreatedTicks != t))
            rows.AddRange(await db.QueryAsync<Expense>("SELECT * FROM Expense WHERE CategoryId = ? AND IsPayback = 0 AND CreatedTicks = ?", categoryId, t));
        await HydrateAsync(db, rows);
        return rows.Where(r => r.BackTotal < r.Amount || r.CreatedTicks == include)
            .OrderByDescending(r => r.Open > 0).ThenByDescending(r => r.Date).ToList();
    }

    /// <summary>Total expected back and not yet returned across all months of a category.</summary>
    public async Task<double> GetOpenAsync(int categoryId)
    {
        var db = await AppDatabase.OpenAsync();
        var rows = await db.QueryAsync<Expense>("SELECT * FROM Expense WHERE CategoryId = ? AND IsPayback = 0 AND ExpectBack = 1 AND WrittenOff = 0", categoryId);
        await HydrateAsync(db, rows);
        return rows.Sum(r => r.Open);
    }

    public async Task SaveAsync(Expense expense)
    {
        var db = await AppDatabase.OpenAsync();
        if (expense.Id == 0)
        {
            expense.CreatedTicks = DateTime.UtcNow.Ticks;
            await db.InsertAsync(expense);
        }
        else await db.UpdateAsync(expense);

        await db.ExecuteAsync("DELETE FROM ExpenseTag WHERE ExpenseId = ?", expense.Id);
        foreach (var tag in expense.Tags.Select(NormalizeTag).Where(t => t.Length > 0).Distinct())
            await db.InsertAsync(new ExpenseTag { ExpenseId = expense.Id, Tag = tag });
    }

    public async Task DeleteAsync(Expense expense)
    {
        var db = await AppDatabase.OpenAsync();
        await db.ExecuteAsync("DELETE FROM ExpenseTag WHERE ExpenseId = ?", expense.Id);
        // Paybacks of a deleted spend stay as plain gains rather than pointing at nothing.
        if (!expense.IsPayback) await db.ExecuteAsync("UPDATE Expense SET SettlesTicks = NULL WHERE SettlesTicks = ?", expense.CreatedTicks);
        await db.DeleteAsync(expense);
        DeleteReceiptFile(expense.Receipt);
    }

    /// <summary>Every tag ever used, most used first.</summary>
    public async Task<List<string>> GetTagSuggestionsAsync()
    {
        var db = await AppDatabase.OpenAsync();
        var rows = await db.QueryAsync<TagUse>("SELECT Tag, COUNT(*) AS Uses FROM ExpenseTag GROUP BY Tag ORDER BY Uses DESC, Tag");
        return rows.Select(r => r.Tag).ToList();
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
