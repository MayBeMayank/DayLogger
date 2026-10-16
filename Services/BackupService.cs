using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DayLogger.Models;
using SQLite;

namespace DayLogger.Services;

/// <summary>A problem the user can act on (corrupt file, wrong password, newer app version).</summary>
public class BackupException(string message) : Exception(message);

/// <summary>The backup is encrypted and no password was given.</summary>
public class PasswordRequiredException() : BackupException("This backup is password protected. Enter its password and try again.");

/// <summary>What a restore added. Anything already on the device is left alone.</summary>
public record RestoreResult(int Meals, int Expenses, int Streaks, int TickDays, int Receipts)
{
    public int Total => Meals + Expenses + Streaks + TickDays;
}

/// <summary>
/// Backup file (.dlbackup): "DLBK1" + flag byte, then either a plain zip or salt/nonce/tag + AES-GCM ciphertext of that zip.
/// The zip holds data.json (all rows) and receipts/. Restore merges: rows are matched by their creation time, so
/// restoring twice, or onto a device that already has some of the data, never duplicates anything.
/// </summary>
public class BackupService
{
    const string Magic = "DLBK1";
    const int Version = 1;
    const int SaltSize = 16, NonceSize = 12, TagSize = 16, Iterations = 200_000;

    static string DbPath => Path.Combine(FileSystem.AppDataDirectory, "daylogger.db3");

    // Backup rows. Separate from the DB models, which carry UI-only members and local ids.
    class MealRow { public string Date { get; set; } = ""; public int Type { get; set; } public int Source { get; set; } public string Dishes { get; set; } = ""; public double? Price { get; set; } public string? Note { get; set; } public long CreatedTicks { get; set; } }
    class ExpenseRow { public string Date { get; set; } = ""; public string Title { get; set; } = ""; public double Amount { get; set; } public string? Description { get; set; } public string? Receipt { get; set; } public long CreatedTicks { get; set; } }
    class StreakRow { public string Name { get; set; } = ""; public int Frequency { get; set; } public long CreatedTicks { get; set; } public long? DeletedTicks { get; set; } }
    class TickRow { public long StreakCreatedTicks { get; set; } public string Date { get; set; } = ""; public int Count { get; set; } public string? Checked { get; set; } }
    class Payload
    {
        public int Version { get; set; } = BackupService.Version;
        public string ExportedUtc { get; set; } = "";
        public List<MealRow> Meals { get; set; } = new();
        public List<ExpenseRow> Expenses { get; set; } = new();
        public List<StreakRow> Streaks { get; set; } = new();
        public List<TickRow> Ticks { get; set; } = new();
    }

    static async Task<SQLiteAsyncConnection> OpenAsync()
    {
        var db = new SQLiteAsyncConnection(DbPath);
        await db.CreateTableAsync<Meal>();
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
            var meals = await db.Table<Meal>().ToListAsync();
            var expenses = await db.Table<Expense>().ToListAsync();
            var streaks = await db.Table<Streak>().ToListAsync();
            var logs = await db.Table<StreakLog>().ToListAsync();
            var ticksById = streaks.ToDictionary(s => s.Id, s => s.CreatedTicks);

            var data = new Payload
            {
                ExportedUtc = DateTime.UtcNow.ToString("o"),
                Meals = meals.Select(m => new MealRow { Date = m.Date, Type = (int)m.Type, Source = (int)m.Source, Dishes = m.Dishes, Price = m.Price, Note = m.Note, CreatedTicks = m.CreatedTicks }).ToList(),
                Expenses = expenses.Select(e => new ExpenseRow { Date = e.Date, Title = e.Title, Amount = e.Amount, Description = e.Description, Receipt = e.Receipt, CreatedTicks = e.CreatedTicks }).ToList(),
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
                    var file = Data.ExpenseRepository.ReceiptPath(name!);
                    if (!File.Exists(file)) continue;
                    // Photos are already compressed.
                    await using var entry = zip.CreateEntry("receipts/" + name, CompressionLevel.NoCompression).Open();
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
                int meals = 0, expenses = 0, streaks = 0, tickDays = 0, receipts = 0;
                await db.RunInTransactionAsync(conn =>
                {
                    var haveMeals = conn.Table<Meal>().ToList().Select(m => (m.Date, m.CreatedTicks)).ToHashSet();
                    foreach (var m in data.Meals.Where(m => !haveMeals.Contains((m.Date, m.CreatedTicks))))
                    {
                        conn.Insert(new Meal { Date = m.Date, Type = (MealType)m.Type, Source = (MealSource)m.Source, Dishes = m.Dishes, Price = m.Price, Note = m.Note, CreatedTicks = m.CreatedTicks });
                        meals++;
                    }

                    var haveExpenses = conn.Table<Expense>().ToList().Select(e => (e.Date, e.CreatedTicks)).ToHashSet();
                    foreach (var e in data.Expenses.Where(e => !haveExpenses.Contains((e.Date, e.CreatedTicks))))
                    {
                        conn.Insert(new Expense { Date = e.Date, Title = e.Title, Amount = e.Amount, Description = e.Description, Receipt = e.Receipt, CreatedTicks = e.CreatedTicks });
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
                return new RestoreResult(meals, expenses, streaks, tickDays, receipts);
            }
            finally { await db.CloseAsync(); }
        }
    }

    static bool RestoreReceipt(ZipArchive zip, string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        // Names are generated GUIDs; never trust a path from a file.
        name = Path.GetFileName(name);
        var entry = zip.GetEntry("receipts/" + name);
        var target = Data.ExpenseRepository.ReceiptPath(name);
        if (entry == null || File.Exists(target)) return false;
        Directory.CreateDirectory(Data.ExpenseRepository.ReceiptsDir);
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
