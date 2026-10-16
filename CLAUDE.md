# DayLogger

Personal daily-logging app built with .NET MAUI (`net10.0`; Android is the primary target, Windows/iOS/MacCatalyst also build). Single project, no tests, no git repo yet. All data is local (SQLite) and nothing leaves the device unless the user exports a backup.

Design language and UI rules live in [DESIGN.md](DESIGN.md). Read it before touching any XAML.

## Build and run

```
dotnet build -f net10.0-android            # primary target
dotnet build -f net10.0-windows10.0.19041.0  # quick desktop check on Windows
```

The project is usually run from Visual Studio. `bin/`, `obj/` and `.vs/` are build output; never edit or search them.

## Architecture

- `Models/` SQLite-net entities (`[PrimaryKey, AutoIncrement] Id`). UI-only members are marked `[Ignore]`; entities implement `INotifyPropertyChanged` themselves for UI state (`ShowActions`, `IsExpanded`).
- `Data/` One repository per aggregate, registered as singletons in `MauiProgram.cs`. Each lazily opens `daylogger.db3` in `FileSystem.AppDataDirectory` and calls `CreateTableAsync<T>()` on first use. There is no migration framework: schema changes are made by adding columns/tables that `CreateTableAsync` can apply.
- `Pages/` XAML pages + code-behind, no MVVM framework. Registered as transient services and routed by name in `AppShell.xaml.cs` (`Shell.Current.GoToAsync("route?...")`).
- `Controls/` Custom views (`OneUiScaffold`, `CalendarView`, `ChipGroup`, `TickRow`, `FluidFill`, ...). Prefer reusing these over new one-offs.
- `Services/` `ThemeService` (palette + light/dark), `ReminderService` (+ per-platform `IReminderPlatform`), `BackupService` (.dlbackup), `ShareCardRenderer`.
- `Platforms/` Platform code only (Android alarm receiver, Windows toast).

## Conventions

- Dates are stored as `yyyy-MM-dd` text (`Key(DateOnly)`); month queries use raw SQL range comparisons because sqlite-net can't translate `string.CompareTo`.
- Rows are identified across devices by `CreatedTicks` (UTC ticks at insert). Backup restore merges on it, so never change how it is set and never reuse it.
- Colours come from `ThemeService` keys (`Paper`, `Surface`, `Ink`, `Muted`, `Line`, `Faint`, `Primary`, `OnPrimary`, `Accent`) and are always bound with `DynamicResource`. Never hard-code a colour in XAML or code.
- Backup rows are separate DTOs in `BackupService`, not the DB models. When a model changes, update the DTO, `CreateAsync`, `RestoreAsync` and bump `Version` only if old readers would break.
- Keep comments sparse and explanatory (why, not what), matching the existing files.
- Receipts are stored as file names (not paths) under `receipts/` in app data.

## Current work: generalized Expenses + Pivot home (in progress)

Decisions already made with the user (do not re-ask):

1. **Meal Tracker is removed.** No data migration; fresh start. Delete `Meal`, `MealRepository`, the Meal pages, the `meal:*` reminders and the Meal parts of `BackupService`. Meals becomes an ordinary user-created category.
2. **Category-based expenses.** `ExpenseCategory` (name, sort order, archived flag, "amount required" toggle) owns user-defined **subcategories**. Each `Expense` has a category, an optional subcategory and optional **tags**.
   - Tags are free-typed; the app remembers previously used tags and suggests them. They are shared across all categories (so later analysis can group by tag globally).
   - Store category/subcategory/tags as separate columns or tables, never as one concatenated string. Analysis (distribution by category, subcategory, tag, date range) is future scope and the data model must make it easy.
   - Amount is required or optional per category (Meals: optional).
3. **Category lifecycle.** A category can be archived (history kept, section hidden), deleted after reassigning its entries to another category, or deleted together with its entries.
4. **Home is a Metro Pivot** (see DESIGN.md): sections in order are `streaks`, one section per active expense category, `settings`. Headers loop around, with parallax and a peeking next header. Headers scroll horizontally when there are many. A `+ new category` item sits at the end of the header row (before wrapping to the first section).
5. **Section content is a launcher** into the existing calendar and day pages, now parameterized by category. The user may ask for richer inline content after trying it, so keep section bodies easy to swap.
6. **`+` button** in every section opens that section's add flow (new streak, or new entry in that category). There are no inline "add" forms on the home page.
7. **Settings section** holds Reminders, Backup & Restore, Streak Bin, theme toggle and category management.
8. **Reminders become per category** ("remind me to log Meals at 21:00"), replacing the hard-coded meal and expense reminders.
9. **Old backups** containing meals do not need to import.

## Gotchas

- `OneUiScaffold` replaces the Shell nav bar when the page is tall enough; pages inside the pivot must not use it (the pivot owns the header).
- `Entry` is made borderless globally in `MauiProgram.cs`; wrap entries in a styled `Border`.
- Reminder alarms are one-pending-per-key, rescheduled on fire. Call `ReminderService.RefreshAllAsync()` after any change that affects them.
