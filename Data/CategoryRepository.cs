using DayLogger.Models;

namespace DayLogger.Data;

public class CategoryRepository
{
    public async Task<List<ExpenseCategory>> GetAllAsync(bool includeArchived = false)
    {
        var db = await AppDatabase.OpenAsync();
        var sql = includeArchived
            ? "SELECT * FROM ExpenseCategory ORDER BY SortOrder, Id"
            : "SELECT * FROM ExpenseCategory WHERE Archived = 0 ORDER BY SortOrder, Id";
        return await db.QueryAsync<ExpenseCategory>(sql);
    }

    public async Task<ExpenseCategory?> GetAsync(int id) =>
        await (await AppDatabase.OpenAsync()).Table<ExpenseCategory>().Where(c => c.Id == id).FirstOrDefaultAsync();

    /// <summary>Adds a category after all existing ones, with its first subcategories.</summary>
    public async Task<ExpenseCategory> CreateAsync(string name, bool amountRequired, IEnumerable<string> subcategories)
    {
        var db = await AppDatabase.OpenAsync();
        var next = await db.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM ExpenseCategory");
        var category = new ExpenseCategory { Name = name.Trim(), AmountRequired = amountRequired, SortOrder = next, CreatedTicks = DateTime.UtcNow.Ticks };
        await db.InsertAsync(category);
        await SaveSubcategoriesAsync(category.Id, subcategories.Select(s => (0, s)).ToList());
        return category;
    }

    public async Task SaveAsync(ExpenseCategory category) => await (await AppDatabase.OpenAsync()).UpdateAsync(category);

    public async Task<List<ExpenseSubcategory>> GetSubcategoriesAsync(int categoryId) =>
        await (await AppDatabase.OpenAsync()).QueryAsync<ExpenseSubcategory>(
            "SELECT * FROM ExpenseSubcategory WHERE CategoryId = ? ORDER BY SortOrder, Id", categoryId);

    /// <summary>
    /// Makes the category's subcategories match <paramref name="items"/> (id 0 = new), in that order. One that was left out is
    /// removed and its entries simply lose their subcategory.
    /// </summary>
    public async Task SaveSubcategoriesAsync(int categoryId, List<(int Id, string Name)> items)
    {
        var db = await AppDatabase.OpenAsync();
        var existing = await GetSubcategoriesAsync(categoryId);
        var keep = items.Where(i => i.Id != 0).Select(i => i.Id).ToHashSet();
        foreach (var gone in existing.Where(e => !keep.Contains(e.Id)))
        {
            await db.ExecuteAsync("UPDATE Expense SET SubcategoryId = NULL WHERE SubcategoryId = ?", gone.Id);
            await db.DeleteAsync(gone);
        }

        int order = 0;
        foreach (var (id, rawName) in items)
        {
            var name = rawName.Trim();
            if (name.Length == 0) continue;
            if (id == 0) await db.InsertAsync(new ExpenseSubcategory { CategoryId = categoryId, Name = name, SortOrder = order });
            else
            {
                var sub = existing.FirstOrDefault(e => e.Id == id);
                if (sub == null) continue;
                sub.Name = name;
                sub.SortOrder = order;
                await db.UpdateAsync(sub);
            }
            order++;
        }
    }

    public async Task SetArchivedAsync(ExpenseCategory category, bool archived)
    {
        category.Archived = archived;
        await SaveAsync(category);
    }

    /// <summary>Swaps the category with its neighbour in the pivot order. Returns false at either end.</summary>
    public async Task<bool> MoveAsync(ExpenseCategory category, int delta)
    {
        var db = await AppDatabase.OpenAsync();
        var all = await GetAllAsync(includeArchived: true);
        int at = all.FindIndex(c => c.Id == category.Id), to = at + delta;
        if (at < 0 || to < 0 || to >= all.Count) return false;
        (all[at], all[to]) = (all[to], all[at]);
        for (int i = 0; i < all.Count; i++)
        {
            all[i].SortOrder = i;
            await db.UpdateAsync(all[i]);
        }
        category.SortOrder = all[to].SortOrder;
        return true;
    }

    public async Task<int> CountEntriesAsync(int categoryId) =>
        await (await AppDatabase.OpenAsync()).ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Expense WHERE CategoryId = ?", categoryId);

    /// <summary>Moves every entry to another category (subcategories don't carry over), then deletes this one.</summary>
    public async Task DeleteAndReassignAsync(ExpenseCategory category, int targetCategoryId)
    {
        var db = await AppDatabase.OpenAsync();
        await db.ExecuteAsync("UPDATE Expense SET CategoryId = ?, SubcategoryId = NULL WHERE CategoryId = ?", targetCategoryId, category.Id);
        await RemoveAsync(db, category);
    }

    /// <summary>Deletes the category together with all its entries, tags and receipt photos.</summary>
    public async Task DeleteWithEntriesAsync(ExpenseCategory category)
    {
        var db = await AppDatabase.OpenAsync();
        var entries = await db.QueryAsync<Expense>("SELECT * FROM Expense WHERE CategoryId = ?", category.Id);
        foreach (var e in entries)
        {
            await db.ExecuteAsync("DELETE FROM ExpenseTag WHERE ExpenseId = ?", e.Id);
            ExpenseRepository.DeleteReceiptFile(e.Receipt);
        }
        await db.ExecuteAsync("DELETE FROM Expense WHERE CategoryId = ?", category.Id);
        await RemoveAsync(db, category);
    }

    static async Task RemoveAsync(SQLite.SQLiteAsyncConnection db, ExpenseCategory category)
    {
        await db.ExecuteAsync("DELETE FROM ExpenseSubcategory WHERE CategoryId = ?", category.Id);
        await db.DeleteAsync(category);
    }
}
