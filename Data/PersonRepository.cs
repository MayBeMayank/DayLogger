using DayLogger.Models;

namespace DayLogger.Data;

public class PersonRepository
{
    readonly ExpenseRepository _expenses;

    public PersonRepository(ExpenseRepository expenses) => _expenses = expenses;

    /// <summary>Everyone, most recently used first (so suggestions favour who you deal with now).</summary>
    public async Task<List<Person>> GetAllAsync() =>
        await (await AppDatabase.OpenAsync()).QueryAsync<Person>(
            "SELECT * FROM Person ORDER BY COALESCE((SELECT MAX(CreatedTicks) FROM Expense WHERE PersonId = Person.Id), 0) DESC, Name COLLATE NOCASE");

    public async Task<Person?> GetAsync(int id) =>
        await (await AppDatabase.OpenAsync()).Table<Person>().Where(p => p.Id == id).FirstOrDefaultAsync();

    /// <summary>Finds a person by name (ignoring case) or adds one; a phone fills in a missing number.</summary>
    public async Task<Person> FindOrCreateAsync(string name, string? phone = null)
    {
        var db = await AppDatabase.OpenAsync();
        name = name.Trim();
        var found = (await db.QueryAsync<Person>("SELECT * FROM Person WHERE Name = ? COLLATE NOCASE LIMIT 1", name)).FirstOrDefault();
        if (found != null)
        {
            if (string.IsNullOrWhiteSpace(found.Phone) && !string.IsNullOrWhiteSpace(phone))
            {
                found.Phone = phone.Trim();
                await db.UpdateAsync(found);
            }
            return found;
        }
        var person = new Person { Name = name, Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(), CreatedTicks = DateTime.UtcNow.Ticks };
        await db.InsertAsync(person);
        return person;
    }

    public async Task SaveAsync(Person person)
    {
        var db = await AppDatabase.OpenAsync();
        if (person.Id == 0)
        {
            person.CreatedTicks = DateTime.UtcNow.Ticks;
            await db.InsertAsync(person);
        }
        else await db.UpdateAsync(person);
    }

    /// <summary>Removes the person; their entries stay, just without a "with".</summary>
    public async Task DeleteAsync(Person person)
    {
        var db = await AppDatabase.OpenAsync();
        await db.ExecuteAsync("UPDATE Expense SET PersonId = NULL WHERE PersonId = ?", person.Id);
        await db.DeleteAsync(person);
    }

    public async Task<List<PersonSummary>> GetSummariesAsync()
    {
        var people = await GetAllAsync();
        var rows = await _expenses.GetByPersonAsync(null);
        return people.Select(p => PersonSummary.Of(p, rows)).ToList();
    }
}
