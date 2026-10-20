# DayLogger

Personal daily-logging app built with .NET MAUI (`net10.0`; Android is the primary target, Windows/iOS/MacCatalyst also build). Single project, no tests, no git repo yet. All data is local (SQLite) and nothing leaves the device unless the user exports a backup.

Design language and UI rules live in [DESIGN.md](DESIGN.md). Read it before touching any XAML.

## Build and run

```
dotnet build -f net10.0-android -m:1       # primary target
dotnet build -f net10.0-windows10.0.19041.0  # quick desktop check on Windows
```

Use `-m:1` for Android: parallel MSBuild intermittently fails in native linking/packaging with "file in use" errors that aren't code problems.

The project is usually run from Visual Studio. `bin/`, `obj/` and `.vs/` are build output; never edit or search them.

## Architecture

- `Models/` SQLite-net entities (`[PrimaryKey, AutoIncrement] Id`). UI-only members are marked `[Ignore]`; entities implement `INotifyPropertyChanged` themselves for UI state (`ShowActions`, `IsExpanded`).
- `Data/` One repository per aggregate, registered as singletons in `MauiProgram.cs`, all on `daylogger.db3` in `FileSystem.AppDataDirectory`. Expenses and categories share one connection through `AppDatabase.OpenAsync()`, which creates their tables and runs the one-time clean-up (drops the old `Meal` table, moves pre-category expenses into a default "Expenses" category). `StreakRepository` still opens its own connection. There is no migration framework: schema changes are made by adding columns/tables that `CreateTableAsync` can apply.
- `Pages/` XAML pages + code-behind, no MVVM framework. Registered as transient services and routed by name in `AppShell.xaml.cs` (`Shell.Current.GoToAsync("route?...")`).
- `Controls/` Custom views. `PivotView` (+ `SwipeHost`, `Metro` helpers) is the home screen; `OneUiScaffold` is the one-hand page scaffold used by every inner page; also `CalendarView`, `ChipGroup`, `TickRow`, `FluidFill`. Prefer reusing these over new one-offs.
- `Services/` `ThemeService` (palette + light/dark), `ReminderService` (+ per-platform `IReminderPlatform`), `BackupService` (.dlbackup), `ShareCardRenderer`.
- `Platforms/` Platform code only (Android alarm receiver, Windows toast, and `SwipeHostHandler` on both so a horizontal swipe wins over the sections' vertical scroll; other platforms fall back to a pan gesture).

## Conventions

- Dates are stored as `yyyy-MM-dd` text (`Key(DateOnly)`); month queries use raw SQL range comparisons because sqlite-net can't translate `string.CompareTo`.
- Rows are identified across devices by `CreatedTicks` (UTC ticks at insert). Backup restore merges on it, so never change how it is set and never reuse it.
- Colours come from `ThemeService` keys (`Paper`, `Surface`, `Ink`, `Muted`, `Line`, `Faint`, `Primary`, `OnPrimary`, `Accent`) and are always bound with `DynamicResource`. Never hard-code a colour in XAML or code.
- Backup rows are separate DTOs in `BackupService`, not the DB models. When a model changes, update the DTO, `CreateAsync`, `RestoreAsync` and bump `Version` only if old readers would break.
- Keep comments sparse and explanatory (why, not what), matching the existing files.
- Receipts are stored as file names (not paths) under `receipts/` in app data.

## Expenses and the pivot home (implemented, needs on-device testing)

Decisions made with the user (do not re-ask):

1. **Meal Tracker is removed.** No data migration; fresh start. Meals is an ordinary user-created category. Old backups' meals are not imported (their expenses are, into a default category).
2. **Category-based expenses.** `ExpenseCategory` (name, sort order, archived, "amount required", reminder) owns user-defined `ExpenseSubcategory` rows. Each `Expense` has `CategoryId`, optional `SubcategoryId`, and tags in `ExpenseTag` rows.
   - Tags are free-typed, lower-cased, remembered for suggestions and shared across all categories.
   - Category/subcategory/tags are separate columns/tables, never one string: analysis (distribution by category, subcategory, tag, date range) is future scope and the model must keep it easy.
   - `Amount` of 0 means "none"; categories with "amount required" off allow it.
3. **Category lifecycle** (`CategoryPage`): archive/restore, delete after moving entries to another category, or delete with entries (receipts included), reorder, edit subcategories.
4. **Home is a Metro pivot** (`MainPage` + `PivotView`) with exactly three sections, in this order: `expenses` (opens first, used most), `streaks`, `settings`. Loops, parallax headers, next header peeks, the active header is larger than the others. `streaks` lists every streak (long-press edits it), `expenses` lists every category (long-press edits it), `settings` lists Reminders, Backup & Restore and the theme. Section keys: `streaks`, `expenses`, `settings`.
5. **Sections are lists that launch** the streak page (`streak?id=`), the calendar (`expenses?cat=`) and the day (`expenseday?cat=&date=`) pages. The user may ask for richer inline content after trying it; section bodies are rebuilt in `MainPage.Fill*` and easy to swap.
6. **`+` button** on the pivot opens the active section's add flow: `streaks` (add/edit form page; `streaks?id=` edits and can bin) on streaks, `expenseday?date=<today>&add=1` on expenses (a new entry; the form has a category chip row, so every kind of expense is entered from one place; with no category yet it opens `category?id=0`). It is hidden on `settings`. `ExpenseDayPage` without `cat` is the all-categories daily view (per-category breakdown + total); with `cat` it lists only that category, but the form can still file or **move** an entry to any category (editing an entry and changing the chip moves it, with its paybacks). Saving into a different category than the filter drops the filter. The big total on the expenses section opens the all-categories calendar (`expenses` without `cat`), whose days open the daily view.
7. **Settings section** holds Manage categories (`categories` page: add, edit, reorder, archive, delete), Reminders, Backup & Restore and the theme toggle. The expenses list has no press-and-hold menu any more (archived categories stay listed there). The streak bin is listed on the streaks section.
8. **Reminders are per category** (`cat:<id>` alarm keys, fields on `ExpenseCategory`, edited on `RemindersPage`). Legacy `meal:*`/`expense` alarms are cancelled on refresh.
9. **Backups** are version 2 (categories, subcategories by name, tags). Version 1 files still restore.
10. **Paybacks** (refund, cashback, repayment, gain) are `Expense` rows with `IsPayback` and a positive `Amount`; spending is always `Expense.Net` (paybacks negative). `SettlesTicks` optionally links a payback to the spend it settles by that spend's `CreatedTicks` (never `Id`, so backup restore keeps it); one payback settles at most one spend, several may settle the same one. A payback counts in the month it was received. `ExpectBack` on a spend makes the unreturned rest "open" (shown as "yet to come back" on the category row); `WrittenOff` closes it without inventing money. Deleting a spend unlinks its paybacks. Wording stays generic ("got back", "open"), not loan-specific.

11. **People** (`Person`: name, optional phone, note, `CreatedTicks`) are the shops, vendors and friends an entry was *with*; `Expense.PersonId` is optional and spans categories. DayLogger owns the list: typing a new name in the entry form adds one, and the CONTACTS button copies name and first number from the phone once (needs `READ_CONTACTS`; the app never reads the address book otherwise). Reached from the `people` row at the top of the expenses section (`people`, `person?id=`); each person shows spent / back / open across categories. Backup carries people by `CreatedTicks`. Deleting a person keeps their entries. People have an optional square `Photo` (file name under `people/`, downsized JPEG; from the phone contact on Android via `ContactPhoto`, or chosen on the person page; shown by the `Avatar` control with the first letter as fallback) and backups carry the files. A spend that has paybacks fills its own card left-to-right (`FillGrid`) instead of showing a bar.
12. **Search** (`SearchPage`, route `search`) is its own page, reached from the `search` row on the expenses section (Metro treats search as a full-screen mode, not a control on a list). Free words (AND; `#tag` for tags) match title, note, category, subcategory, person, tags and amount; filters are a date range (presets or two pickers), category and spent/got back. `ExpenseRepository.SearchAsync(ExpenseSearch)` narrows by date/category/kind in SQL and matches words in memory. A result opens its day.
13. **Feature tiles vs categories.** The top of the expenses section is a row of three solid `Primary` square tiles (`Metro.Tile`: live figure top-left, label bottom-left): `search`, `insights`, `people`. Below a `CATEGORIES` heading come the user's categories as plain text rows. Keep that split: app features are tiles, user data is text.
14. **Insights** (`InsightsPage`, route `insights`): date range chips (shared `DateRanges`) and a BY chip (category, subcategory, tag, person, month) over `ExpenseRepository.SearchAsync`; `Breakdown.Of` groups entries (paybacks negative), shown as flat star-column bars with share and entry count (no share on tags, which overlap; long tail folds into "N more"). A bar opens `search?q=&cat=&from=&to=` (person/subcategory/tag drill by text, so a title containing the same word can also appear).
Not verified on a device yet: the swipe interception in `SwipeHostHandler` (Android/Windows), header parallax feel, light-font availability per platform. These compile on Android and Windows; iOS/MacCatalyst are untested.

12. **Quick-add widget** (Android only, `Platforms/Android/QuickAddWidget.cs`): a home-screen button that opens the new-expense form. It carries no data. It launches `MainActivity` with `daylogger://add/expense`; `QuickAdd` (Services) hands that to `MainPage`, which waits for a cold start and listens when running. A second widget, **quick entry** (`QuickEntryWidget` + `QuickEntryActivity`), opens a native translucent sheet over the home screen (category, subcategory, amount, title; blank title falls back to the subcategory or category name; "more" opens the full form). Widgets cannot hold text boxes, hence the sheet. It is drawn with native views using `ThemeService` colours and saves through `ExpenseRepository` directly; it shares the `expenses.lastCat` / `expenses.lastSub.<id>` preferences with the day page. Richer widgets (tick a streak, today's total, per-category buttons) are future scope.

## Gotchas

- `OneUiScaffold` replaces the Shell nav bar when the page is tall enough and sets its title in lowercase light type. Every pushed page uses it; `MainPage` does not (the pivot owns the header, and `Shell.NavBarIsVisible="False"`).
- `Entry` is made borderless globally in `MauiProgram.cs`; wrap entries in a styled `Border`.
- **Press-and-hold menus** use `ActionBar` (Metro app-bar: round icon buttons in a bar that slides up from the bottom) shown by `LongPressBehavior`. Never put a `TapGestureRecognizer` on the same view as a `LongPressBehavior` on Android: the recognizer swallows touch events, so the native long-press never fires. Use `LongPressBehavior { HandleTap = true }` and its `Tapped` event instead (`Metro.ListItem(..., longPress:)` does this).
- **Keyboard:** pages with text inputs set `SafeAreaEdges="SoftInput"` (Android is edge-to-edge, so a page must opt in to avoid the keyboard). To autofocus an entry when a form opens use `entry.FocusSoon()`, not `Focus()` in `OnAppearing`, which is too early on Android.
- The home lists show a back-to-top button (bottom-left, mirrors `+`) once a section is scrolled more than 300dp.
- Reminder alarms are one-pending-per-key, rescheduled on fire. Call `ReminderService.RefreshAllAsync()` after any change that affects them.
