using DayLogger.Models;

namespace DayLogger.Data;

public class PersonRepository
{
    readonly ExpenseRepository _expenses;

    public PersonRepository(ExpenseRepository expenses) => _expenses = expenses;

    public static string PhotosDir => Path.Combine(FileSystem.AppDataDirectory, "people");
    public static string PhotoPath(string fileName) => Path.Combine(PhotosDir, fileName);

    /// <summary>Saves a picked image as a small JPEG (a few hundred px is plenty for a square avatar) and returns its file name.</summary>
    public static async Task<string> StorePhotoAsync(byte[] image)
    {
        Directory.CreateDirectory(PhotosDir);
        var name = $"{Guid.NewGuid():N}.jpg";
        byte[] small;
        try
        {
            using var source = Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(new MemoryStream(image));
            using var scaled = source.Downsize(320f, false);
            small = scaled.AsBytes(Microsoft.Maui.Graphics.ImageFormat.Jpeg, 0.85f);
        }
        catch { small = image; } // an odd format: keep it as it came rather than lose the photo
        await File.WriteAllBytesAsync(PhotoPath(name), small);
        return name;
    }

    public static void DeletePhotoFile(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return;
        try { File.Delete(PhotoPath(fileName)); } catch { /* already gone */ }
    }

    /// <summary>Replaces (or with null removes) a person's photo.</summary>
    public async Task SetPhotoAsync(Person person, byte[]? image)
    {
        var old = person.Photo;
        person.Photo = image == null ? null : await StorePhotoAsync(image);
        await (await AppDatabase.OpenAsync()).UpdateAsync(person);
        DeletePhotoFile(old);
    }

    /// <summary>Everyone, most recently used first (so suggestions favour who you deal with now).</summary>
    public async Task<List<Person>> GetAllAsync() =>
        await (await AppDatabase.OpenAsync()).QueryAsync<Person>(
            "SELECT * FROM Person ORDER BY COALESCE((SELECT MAX(CreatedTicks) FROM Expense WHERE PersonId = Person.Id), 0) DESC, Name COLLATE NOCASE");

    public async Task<Person?> GetAsync(int id) =>
        await (await AppDatabase.OpenAsync()).Table<Person>().Where(p => p.Id == id).FirstOrDefaultAsync();

    /// <summary>Finds a person by name (ignoring case) or adds one; a phone fills in a missing number.</summary>
    public async Task<Person> FindOrCreateAsync(string name, string? phone = null, byte[]? photo = null)
    {
        var db = await AppDatabase.OpenAsync();
        name = name.Trim();
        var found = (await db.QueryAsync<Person>("SELECT * FROM Person WHERE Name = ? COLLATE NOCASE LIMIT 1", name)).FirstOrDefault();
        if (found != null)
        {
            bool changed = false;
            if (string.IsNullOrWhiteSpace(found.Phone) && !string.IsNullOrWhiteSpace(phone)) { found.Phone = phone.Trim(); changed = true; }
            if (found.Photo == null && photo != null) { found.Photo = await StorePhotoAsync(photo); changed = true; }
            if (changed) await db.UpdateAsync(found);
            return found;
        }
        var person = new Person { Name = name, Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(), CreatedTicks = DateTime.UtcNow.Ticks };
        if (photo != null) person.Photo = await StorePhotoAsync(photo);
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
        DeletePhotoFile(person.Photo);
    }

    public async Task<List<PersonSummary>> GetSummariesAsync()
    {
        var people = await GetAllAsync();
        var rows = await _expenses.GetByPersonAsync(null);
        return people.Select(p => PersonSummary.Of(p, rows)).ToList();
    }
}
