using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DayLogger.Data;
using DayLogger.Models;
using SQLite;

namespace DayLogger.Services;

/// <summary>A problem the user can act on (corrupt file, wrong password, newer app version).</summary>
public class BackupException(string message) : Exception(message);

/// <summary>The backup is encrypted and no password was given.</summary>
public class PasswordRequiredException() : BackupException("This backup is password protected. Enter its password and try again.");

/// <summary>What a restore added. Anything already on the device is left alone.</summary>
public record RestoreResult(int Categories, int Expenses, int Streaks, int TickDays, int Receipts)
{
    public int Total => Categories + Expenses + Streaks + TickDays;
}

/// <summary>
/// Backup file (.dlbackup): "DLBK1" + flag byte, then either a plain zip or salt/nonce/tag + AES-GCM ciphertext of that zip.
/// The zip holds data.json (all rows) and receipts/. Restore merges: rows are matched by their creation time, so
/// restoring twice, or onto a device that already has some of the data, never duplicates anything.
/// Version 2 added expense categories; older files restore their expenses into one default category (their meals are not read).
/// </summary>
public class BackupService
{
    const string Magic = "DLBK1";
    const int Version = 2;
    const int SaltSize = 16, NonceSize = 12, TagSize = 16, Iterations = 200_000;

    static string DbPath => Path.Combine(FileSystem.AppDataDirectory, "daylogger.db3");

    // Backup rows. Separate from the DB models, which carry UI-only members and local ids.
    // Subcategories travel by name inside their category; an expense points at its category by the category's creation time.
    class CategoryRow
    {
        public string Name { get; set; } = "";
        public int SortOrder { get; set; }
        public bool Archived { get; set; }
        public bool AmountRequired { get; set; } = true;
        public bool ReminderOn { get; set; }
        public int ReminderMinutes { get; set; } = 21 * 60 + 30;
        public long CreatedTicks { get; set; }
        public List<string> Subcategories { get; set; } = new();
    }

    class PersonRow
    {
        public string Name { get; set; } = "";
        public string? Phone { get; set; }
        public string? Note { get; set; }
        public string? Photo { get; set; }
        public long CreatedTicks { get; set; }
    }

    class ExpenseRow
    {
        public string Date { get; set; } = "";
        public string Title { get; set; } = "";
        public double Amount { get; set; }
        public string? Description { get; set; }
        public string? Receipt { get; set; }
        public long CreatedTicks { get; set; }
        public long CategoryCreatedTicks { get; set; }
        public string? Subcategory { get; set; }
        public long? PersonCreatedTicks { get; set; }
        public bool IsPayback { get; set; }
        public long? SettlesTicks { get; set; }
        public bool ExpectBack { get; set; }
        public bool WrittenOff { get; set; }
        public List<string> Tags { get; set; } = new();
    }

    class StreakRow { public string Name { get; set; } = ""; public int Frequency { get; set; } public long CreatedTicks { get; set; } public long? DeletedTicks { get; set; } }
    class TickRow { public long StreakCreatedTicks { get; set; } public string Date { get; set; } = ""; public int Count { get; set; } public string? Checked { get; set; } }
    class Payload
    {
        public int Version { get; set; } = BackupService.Version;
        public string ExportedUtc { get; set; } = "";
        public List<CategoryRow> Categories { get; set; } = new();
        public List<PersonRow> People { get; set; } = new();
        public List<ExpenseRow> Expenses { get; set; } = new();
        public List<StreakRow> Streaks { get; set; } = new();
        public List<TickRow> Ticks { get; set; } = new();
    }

    static async Task<SQLiteAsyncConnection> OpenAsync()
    {
        var db = new SQLiteAsyncConnection(DbPath);
        await db.CreateTableAsync<ExpenseCategory>();
        await db.CreateTableAsync<ExpenseSubcategory>();
        await db.CreateTableAsync<ExpenseTag>();
        await db.CreateTableAsync<Person>();
        await db.CreateTableAsync<Expense>();
        await db.CreateTableAsync<Streak>();
        await db.CreateTableAsync<StreakLog>();
        return db;
    }

    /// <summary>Writes a backup file into the cache folder and returns its path, ready to share.</summary>
    public async Task<string> CreateAsync(string? password)
    {
        var db = await OpenAsync();
        try
        {
            var categories = await db.Table<ExpenseCategory>().ToListAsync();
            var subs = await db.Table<ExpenseSubcategory>().ToListAsync();
            var tags = await db.Table<ExpenseTag>().ToListAsync();
            var expenses = await db.Table<Expense>().ToListAsync();
            var people = await db.Table<Person>().ToListAsync();
            var personTicks = people.ToDictionary(p => p.Id, p => p.CreatedTicks);
            var streaks = await db.Table<Streak>().ToListAsync();
            var logs = await db.Table<StreakLog>().ToListAsync();
            var ticksById = streaks.ToDictionary(s => s.Id, s => s.CreatedTicks);
            var categoryTicks = categories.ToDictionary(c => c.Id, c => c.CreatedTicks);
            var subName = subs.ToDictionary(x => x.Id, x => x.Name);

            var data = new Payload
            {
                ExportedUtc = DateTime.UtcNow.ToString("o"),
                Categories = categories.Select(c => new CategoryRow
                {
                    Name = c.Name, SortOrder = c.SortOrder, Archived = c.Archived, AmountRequired = c.AmountRequired,
                    ReminderOn = c.ReminderOn, ReminderMinutes = c.ReminderMinutes, CreatedTicks = c.CreatedTicks,
                    Subcategories = subs.Where(x => x.CategoryId == c.Id).OrderBy(x => x.SortOrder).Select(x => x.Name).ToList(),
                }).ToList(),
                People = people.Select(p => new PersonRow { Name = p.Name, Phone = p.Phone, Note = p.Note, Photo = p.Photo, CreatedTicks = p.CreatedTicks }).ToList(),
                Expenses = expenses.Select(e => new ExpenseRow
                {
                    PersonCreatedTicks = e.PersonId is { } pid && personTicks.TryGetValue(pid, out var pt) ? pt : null,
                    Date = e.Date, Title = e.Title, Amount = e.Amount, Description = e.Description, Receipt = e.Receipt, CreatedTicks = e.CreatedTicks,
                    CategoryCreatedTicks = categoryTicks.GetValueOrDefault(e.CategoryId),
                    Subcategory = e.SubcategoryId is { } sid ? subName.GetValueOrDefault(sid) : null,
                    IsPayback = e.IsPayback, SettlesTicks = e.SettlesTicks, ExpectBack = e.ExpectBack, WrittenOff = e.WrittenOff,
                    Tags = tags.Where(t => t.ExpenseId == e.Id).Select(t => t.Tag).ToList(),
                }).ToList(),
                Streaks = streaks.Select(s => new StreakRow { Name = s.Name, Frequency = s.Frequency, CreatedTicks = s.CreatedTicks, DeletedTicks = s.DeletedTicks }).ToList(),
                Ticks = logs.Where(l => ticksById.ContainsKey(l.StreakId))
                            .Select(l => new TickRow { StreakCreatedTicks = ticksById[l.StreakId], Date = l.Date, Count = l.Count, Checked = l.Checked }).ToList(),
            };

            using var zipBytes = new MemoryStream();
            using (var zip = new ZipArchive(zipBytes, ZipArchiveMode.Create, leaveOpen: true))
            {
                await using (var json = zip.CreateEntry("data.json").Open())
                    await JsonSerializer.SerializeAsync(json, data);

                foreach (var name in expenses.Select(e => e.Receipt).Where(r => !string.IsNullOrEmpty(r)).Distinct())
                {
                    var file = ExpenseRepository.ReceiptPath(name!);
                    if (!File.Exists(file)) continue;
                    // Photos are already compressed.
                    await using var entry = zip.CreateEntry("receipts/" + name, CompressionLevel.NoCompression).Open();
                    await using var src = File.OpenRead(file);
                    await src.CopyToAsync(entry);
                }

                foreach (var name in people.Select(p => p.Photo).Where(r => !string.IsNullOrEmpty(r)).Distinct())
                {
                    var file = PersonRepository.PhotoPath(name!);
                    if (!File.Exists(file)) continue;
                    await using var entry = zip.CreateEntry("people/" + name, CompressionLevel.NoCompression).Open();
                    await using var src = File.OpenRead(file);
                    await src.CopyToAsync(entry);
                }
            }

            var path = Path.Combine(FileSystem.CacheDirectory, $"DayLogger-{DateTime.Now:yyyyMMdd-HHmm}.dlbackup");
            await File.WriteAllBytesAsync(path, Seal(zipBytes.ToArray(), password));
            return path;
        }
        finally { await db.CloseAsync(); }
    }

    /// <summary>Merges a backup into the database. Existing data is never changed or removed.</summary>
    public async Task<RestoreResult> RestoreAsync(byte[] file, string? password)
    {
        var zipBytes = Unseal(file, password);
        ZipArchive zip;
        try { zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read); }
        catch (InvalidDataException) { throw new BackupException("This file is damaged and can't be read."); }

        using (zip)
        {
            var entry = zip.GetEntry("data.json") ?? throw new BackupException("This isn't a DayLogger backup.");
            Payload data;
            try
            {
                await using var s = entry.Open();
                data = await JsonSerializer.DeserializeAsync<Payload>(s) ?? throw new BackupException("This backup is empty.");
            }
            catch (JsonException) { throw new BackupException("This file is damaged and can't be read."); }

            if (data.Version > Version) throw new BackupException("This backup was made by a newer version of DayLogger. Update the app first.");

            var db = await OpenAsync();
            try
            {
                int categories = 0, expenses = 0, streaks = 0, tickDays = 0, receipts = 0;
                await db.RunInTransactionAsync(conn =>
                {
                    // A category is identified by when it was created, so a rename on either side doesn't duplicate it.
                    var categoryByTicks = conn.Table<ExpenseCategory>().ToList().ToDictionary(c => c.CreatedTicks, c => c);
                    foreach (var c in data.Categories)
                    {
                        if (!categoryByTicks.TryGetValue(c.CreatedTicks, out var category))
                        {
                            category = new ExpenseCategory
                            {
                                Name = c.Name, SortOrder = c.SortOrder, Archived = c.Archived, AmountRequired = c.AmountRequired,
                                ReminderOn = c.ReminderOn, ReminderMinutes = c.ReminderMinutes, CreatedTicks = c.CreatedTicks,
                            };
                            conn.Insert(category);
                            categoryByTicks[c.CreatedTicks] = category;
                            categories++;
                        }
                        foreach (var name in c.Subcategories) EnsureSubcategory(conn, category.Id, name);
                    }

                    // Backups from before categories existed carry expenses with no category: they go into one default one.
                    ExpenseCategory? fallback = null;
                    ExpenseCategory CategoryFor(long ticks)
                    {
                        if (categoryByTicks.TryGetValue(ticks, out var known)) return known;
                        if (fallback != null) return fallback;
                        fallback = conn.Table<ExpenseCategory>().ToList().FirstOrDefault(x => x.Name == AppDatabase.DefaultCategoryName);
                        if (fallback == null)
                        {
                            fallback = new ExpenseCategory { Name = AppDatabase.DefaultCategoryName, CreatedTicks = DateTime.UtcNow.Ticks };
                            conn.Insert(fallback);
                            categories++;
                        }
                        return fallback;
                    }

                    // People are matched by creation time too, so a rename on either side doesn't duplicate them.
                    var personByTicks = conn.Table<Person>().ToList().ToDictionary(p => p.CreatedTicks, p => p);
                    foreach (var p in data.People.Where(p => !personByTicks.ContainsKey(p.CreatedTicks)))
                    {
                        var person = new Person { Name = p.Name, Phone = p.Phone, Note = p.Note, Photo = RestoreFile(zip, "people", p.Photo, PersonRepository.PhotosDir) ? Path.GetFileName(p.Photo) : null, CreatedTicks = p.CreatedTicks };
                        conn.Insert(person);
                        personByTicks[p.CreatedTicks] = person;
                    }

                    var haveExpenses = conn.Table<Expense>().ToList().Select(e => (e.Date, e.CreatedTicks)).ToHashSet();
                    foreach (var e in data.Expenses.Where(e => !haveExpenses.Contains((e.Date, e.CreatedTicks))))
                    {
                        var category = CategoryFor(e.CategoryCreatedTicks);
                        var row = new Expense
                        {
                            Date = e.Date, Title = e.Title, Amount = e.Amount, Description = e.Description, Receipt = e.Receipt, CreatedTicks = e.CreatedTicks,
                            CategoryId = category.Id,
                            PersonId = e.PersonCreatedTicks is { } wt && personByTicks.TryGetValue(wt, out var who) ? who.Id : null,
                            IsPayback = e.IsPayback, SettlesTicks = e.SettlesTicks, ExpectBack = e.ExpectBack, WrittenOff = e.WrittenOff,
                            SubcategoryId = string.IsNullOrWhiteSpace(e.Subcategory) ? null : EnsureSubcategory(conn, category.Id, e.Subcategory),
                        };
                        conn.Insert(row);
                        foreach (var tag in e.Tags.Select(ExpenseRepository.NormalizeTag).Where(t => t.Length > 0).Distinct())
                            conn.Insert(new ExpenseTag { ExpenseId = row.Id, Tag = tag });
                        expenses++;
                        if (RestoreReceipt(zip, e.Receipt)) receipts++;
                    }

                    // A streak is identified by when it was created, so a rename on either side doesn't duplicate it.
                    var idByCreated = conn.Table<Streak>().ToList().ToDictionary(s => s.CreatedTicks, s => s.Id);
                    foreach (var s in data.Streaks.Where(s => !idByCreated.ContainsKey(s.CreatedTicks)))
                    {
                        var row = new Streak { Name = s.Name, Frequency = s.Frequency, CreatedTicks = s.CreatedTicks, DeletedTicks = s.DeletedTicks };
                        conn.Insert(row);
                        idByCreated[s.CreatedTicks] = row.Id;
                        streaks++;
                    }

                    // Same streak and day on both sides: keep every box ticked on either.
                    foreach (var t in data.Ticks)
                    {
                        if (!idByCreated.TryGetValue(t.StreakCreatedTicks, out var streakId)) continue;
                        var incoming = new StreakLog { Count = t.Count, Checked = t.Checked }.Indices;
                        var existing = conn.Table<StreakLog>().Where(l => l.StreakId == streakId && l.Date == t.Date).FirstOrDefault();
                        if (existing == null)
                        {
                            conn.Insert(new StreakLog { StreakId = streakId, Date = t.Date, Indices = incoming });
                            tickDays++;
                            continue;
                        }
                        var merged = existing.Indices.Union(incoming).ToHashSet();
                        if (merged.Count == existing.Indices.Count) continue;
                        existing.Indices = merged;
                        conn.Update(existing);
                        tickDays++;
                    }
                });
                return new RestoreResult(categories, expenses, streaks, tickDays, receipts);
            }
            finally { await db.CloseAsync(); }
        }
    }

    /// <summary>Id of the named subcategory in a category, creating it at the end if it isn't there.</summary>
    static int EnsureSubcategory(SQLiteConnection conn, int categoryId, string name)
    {
        name = name.Trim();
        var existing = conn.Table<ExpenseSubcategory>().Where(x => x.CategoryId == categoryId).ToList();
        var found = existing.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (found != null) return found.Id;
        var sub = new ExpenseSubcategory { CategoryId = categoryId, Name = name, SortOrder = existing.Count };
        conn.Insert(sub);
        return sub.Id;
    }

    static bool RestoreReceipt(ZipArchive zip, string? name) => RestoreFile(zip, "receipts", name, ExpenseRepository.ReceiptsDir);

    /// <summary>Copies a picture out of the zip into its app folder; true when the file is there afterwards.</summary>
    static bool RestoreFile(ZipArchive zip, string folder, string? name, string targetDir)
    {
        if (string.IsNullOrEmpty(name)) return false;
        // Names are generated GUIDs; never trust a path from a file.
        name = Path.GetFileName(name);
        var target = Path.Combine(targetDir, name);
        if (File.Exists(target)) return true;
        var entry = zip.GetEntry(folder + "/" + name);
        if (entry == null) return false;
        Directory.CreateDirectory(targetDir);
        entry.ExtractToFile(target);
        return true;
    }

    static byte[] Seal(byte[] zip, string? password)
    {
        var header = System.Text.Encoding.ASCII.GetBytes(Magic).Append((byte)(password == null ? 0 : 1)).ToArray();
        if (password == null) return header.Concat(zip).ToArray();

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipher = new byte[zip.Length];
        using (var aes = new AesGcm(DeriveKey(password, salt), TagSize))
            aes.Encrypt(nonce, zip, cipher, tag, header);
        return header.Concat(salt).Concat(nonce).Concat(tag).Concat(cipher).ToArray();
    }

    static byte[] Unseal(byte[] file, string? password)
    {
        int headerLen = Magic.Length + 1;
        if (file.Length < headerLen || System.Text.Encoding.ASCII.GetString(file, 0, Magic.Length) != Magic)
            throw new BackupException("This isn't a DayLogger backup.");

        if (file[Magic.Length] == 0) return file[headerLen..];

        if (string.IsNullOrEmpty(password)) throw new PasswordRequiredException();
        if (file.Length < headerLen + SaltSize + NonceSize + TagSize) throw new BackupException("This file is damaged and can't be read.");

        var salt = file.AsSpan(headerLen, SaltSize);
        var nonce = file.AsSpan(headerLen + SaltSize, NonceSize);
        var tag = file.AsSpan(headerLen + SaltSize + NonceSize, TagSize);
        var cipher = file.AsSpan(headerLen + SaltSize + NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(DeriveKey(password, salt), TagSize);
            aes.Decrypt(nonce, cipher, tag, plain, file.AsSpan(0, headerLen));
        }
        catch (CryptographicException) { throw new BackupException("Wrong password, or the file is damaged."); }
        return plain;
    }

    static byte[] DeriveKey(string password, ReadOnlySpan<byte> salt) =>
        Rfc2898DeriveBytes.Pbkdf2(System.Text.Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
}
