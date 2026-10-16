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

    static async Task HydrateAsync(SQLiteAsyncConnection db, List<Expense> rows)
    {
        if (rows.Count == 0) return;
        var ids = string.Join(",", rows.Select(r => r.Id));
        var tags = await db.QueryAsync<ExpenseTag>($"SELECT * FROM ExpenseTag WHERE ExpenseId IN ({ids}) ORDER BY Id");
        var subs = (await db.Table<ExpenseSubcategory>().ToListAsync()).ToDictionary(s => s.Id, s => s.Name);
        foreach (var r in rows)
        {
            r.Tags = tags.Where(t => t.ExpenseId == r.Id).Select(t => t.Tag).ToList();
            if (r.SubcategoryId is { } sid && subs.TryGetValue(sid, out var name)) r.SubcategoryName = name;
        }
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
