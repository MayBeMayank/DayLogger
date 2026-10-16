# DayLogger

A personal daily-logging app built with .NET MAUI (`net10.0`). Android is the primary target; Windows, iOS and MacCatalyst also build. All data is stored locally in SQLite and nothing leaves the device unless you export a backup.

The UI follows a Windows Phone "Metro" design language: typography-led, flat, square, one accent colour. See [DESIGN.md](DESIGN.md) for the full rules.

## Features

The home screen is a looping, swipeable **pivot** with three sections:

- **Streaks**: every streak with today's progress. Tap one to open its calendar and tick today; long-press to edit or bin it. Binned streaks are listed at the bottom.
- **Expenses**: user-defined categories with their month totals. Tap one to open its calendar and day pages; long-press to edit, archive or restore it.
  - Each category has its own subcategories, an "amount required" setting and optional reminders.
  - Entries have a subcategory, amount (optional where the category allows), title, free-typed tags, a note and a receipt photo.
  - Categories can be reordered, archived, or deleted (moving their entries elsewhere or deleting them too).
- **Settings**: Reminders (per category), Backup & Restore, and the light/dark theme toggle.

The round `+` button adds to the active section (new streak or new category) and is hidden on Settings. A back-to-top button appears after scrolling a long list.

Other details:

- Reminders use local alarms (Android alarm receiver, Windows toast).
- Backups are `.dlbackup` files (version 2: categories, subcategories, tags). Version 1 files still restore, and restore merges rows rather than replacing them.
- The theme picks up the platform accent (Material You on Android, the Windows accent) with light and dark palettes.

Money that comes back (refunds, cashback, repayments) is logged on the same day pages with the **spent | got back** switch. A payback can be linked to the spend it settles, and spending totals are net of paybacks.

Entries can be linked to a **person** (a shop, a vendor, a friend you lent to), typed or picked from the phone's contacts. The `people` row on the expenses list shows what you spent with each and what is still to come back.

## Build and run

```
dotnet build -f net10.0-android -m:1         # primary target
dotnet build -f net10.0-windows10.0.19041.0  # quick desktop check on Windows
```

Use `-m:1` for Android: parallel MSBuild intermittently fails in native linking/packaging with "file in use" errors that are not code problems. The project is usually run from Visual Studio.

## Project layout

| Folder | Contents |
|---|---|
| `Models/` | SQLite-net entities (`Streak`, `Expense`, `ExpenseCategory`, tags, etc.) |
| `Data/` | One repository per aggregate, registered as singletons; `AppDatabase` shares one connection for expenses and categories |
| `Pages/` | XAML pages and code-behind (no MVVM framework), routed by name in `AppShell.xaml.cs` |
| `Controls/` | Custom views: `PivotView`, `SwipeHost`, `Metro`, `OneUiScaffold`, `ActionBar`, `CalendarView`, `ChipGroup`, `TickRow`, `FluidFill` |
| `Services/` | `ThemeService`, `ReminderService`, `BackupService`, `ShareCardRenderer` |
| `Platforms/` | Platform-only code (alarms, toasts, `SwipeHostHandler` for Android and Windows) |
| `docs/` | Supporting documentation |

## Data

- Everything lives in `daylogger.db3` in the app data directory. There is no migration framework; schema changes add columns or tables that `CreateTableAsync` can apply.
- Dates are stored as `yyyy-MM-dd` text.
- Rows are identified across devices by `CreatedTicks` (UTC ticks at insert), which backup restore merges on.
- Receipts are stored as file names under `receipts/` in app data.
- Category, subcategory and tags are separate columns/tables so that spending analysis can be added later.

## Status

The expenses and pivot home are implemented but still need on-device testing, in particular the swipe interception in `SwipeHostHandler`, header parallax feel, and light-font availability per platform. Android and Windows compile; iOS and MacCatalyst are untested.

Out of scope for now: charts and expense analysis, richer inline content in the home sections, and tablet/landscape layouts.

## Contributing notes

Project conventions and gotchas (theme colour keys, long-press menus, keyboard handling, backup DTOs) are in [CLAUDE.md](CLAUDE.md).
