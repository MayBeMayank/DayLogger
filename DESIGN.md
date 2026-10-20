# DayLogger Design

DayLogger follows the **Windows Phone 8 / Nokia Lumia "Metro"** design language: typography-led, flat, content over chrome. Authentic means restraint: no cards with shadows, no rounded corners, no gradients.

## Principles

1. **Typography is the interface.** Large, light-weight, lowercase headings carry hierarchy instead of boxes and icons.
2. **Flat and square.** Corner radius 0 everywhere. No shadows, outlines only where an input needs one.
3. **Fast and fluid.** Motion explains navigation (parallax, slide, turnstile), never decorates.
4. **Content first.** Colour is used for the accent and state, not for decoration.
5. **One accent.** A single accent colour on a plain light or dark background.

## Home: the Pivot

Home is a Pivot: horizontally swipeable sections, each scrolling vertically on its own.

```
 DAYLOGGER                      <- app title, small caps, 12sp, muted
 Streaks  Expenses  Settings    <- section headers, 40sp active, capitalised, light
 ───────────────────────────
 [ active section content, vertical scroll ]
                              [+]  <- app-bar style add button
```

**Sections (in order):** `streaks`, `expenses`, `settings`. Always these three.
- `streaks` lists every streak with today's progress; tapping one opens its calendar (with today's ticks), long-pressing edits it. The bin is listed at the bottom when it has items.
- `expenses` lists every expense category (user-ordered) with its month total; tapping one opens its calendar, long-pressing edits it. Archived categories stay in the list, marked archived, so they can be restored.
- `settings` lists Reminders, Backup & Restore and the theme.

**Headers**
- Active header: full `Ink` at 40sp. Inactive headers: `Muted` at 85% of that, so the active one reads slightly larger. Size and colour ease between the two as the pivot moves.
- The next header peeks in from the right edge as a swipe hint.
- Headers move at about half the speed of the content (parallax), so the row slides slower than the page.
- The row scrolls horizontally when it doesn't fit. Tapping a header jumps to that section.
- Swiping past the last section wraps to the first, and the reverse. Header order wraps with it.

**Section body**
- A vertical list in the Metro list style: a large primary line, a muted secondary line, 16sp gutters, no dividers.
- Each section is a launcher: month total (for categories), then the entry points into the existing calendar and day pages. The body is deliberately simple so it can be swapped for richer content later.
- Each section keeps its own scroll position when the user swipes away and back.

**Add button**
- A single circular `+` bottom-right, in thumb reach, and it acts on the active section: new streak on `streaks` (the form page, which also edits and bins an existing streak), new category on `expenses` (entries are added from inside a category). Hidden on `settings`.
- No inline add forms on the home page.

**Press-and-hold menu**
- Holding a streak or category slides a Metro app bar up from the bottom edge: round icon buttons with a small lowercase label (✎ edit, 🗑 bin, 🗄 archive / ↩ restore) under the item's name. Tapping outside, or Back, dismisses it. Deleting for good stays inside the edit page.

**Back to top**
- A round `↑` button mirrors `+` at the bottom left. It appears once the active section has scrolled past 300dp and scrolls that section to the top.

**One-handed use**
- The app title sits near the top (about 3% of the height), the headers follow, the section body fills the rest, and `+` sits at the bottom right. Pushed pages keep `OneUiScaffold`, whose large title sits low and collapses on scroll.

## Typography

| Role | Size | Weight | Notes |
|---|---|---|---|
| Pivot header | 40sp | Light | First letter capitalised |
| Page title | 32sp | Light | lowercase |
| List primary | 20sp | Regular | |
| List secondary | 14sp | Regular | `Muted` |
| App title | 12sp | Semibold | UPPERCASE, letter-spaced, `Muted` |
| Body / input | 14–16sp | Regular | |

Font: Segoe UI (Light, Semilight, Regular) on Windows. On Android use the system light sans (`sans-serif-light`). Bundled `OpenSans` stays as the fallback. Page titles are lowercase; pivot headers capitalise only the first letter.

## Colour

Colours come from `ThemeService` keys and are always bound with `DynamicResource` (see CLAUDE.md). The key set is unchanged: `Paper`, `Surface`, `Ink`, `Muted`, `Line`, `Faint`, `Primary`, `OnPrimary`, `Accent`.

- Page background is `Paper`: pure black in dark mode, white in light mode (Metro, not tinted greys).
- `Primary` is the single accent and is used for the active state, the `+` button, links and progress.
- `Surface` is only for tiles that need to separate from the page; most content sits directly on `Paper`.
- Platform accent sourcing (Material You, Windows accent) is kept. Only the geometry changes.

## Shape, spacing and components

- Corner radius is 0 on `Border`, `Button` and inputs. This replaces the current 12/16/20 radii in `Styles.xaml`.
- Page gutter 16dp (Metro used 12/24). Vertical rhythm in multiples of 8.
- **Tiles** (for settings entries and category management): flat `Accent` fill, square, label bottom-left in the primary text size.
- **Buttons:** flat rectangles, uppercase 12sp semibold label, 2px outline in `Ink` for secondary, `Primary` fill for primary.
- **Inputs:** underline-free filled rectangle with `Surface` background and a 2px `Primary` border while focused. Labels are small and muted above the input.
- **Toggles / choices:** Metro toggle switch, text labels "on" / "off".
- **Chips (subcategory and tag pickers):** flat rectangles, no radius, filled with `Primary` when selected.
- **Confirmations:** full-width bottom bar or a centred modal with two flat text buttons. No bubbly dialogs.
- **Destructive actions** (delete a category with entries, empty the bin) always name the consequence and use the `Primary` button for the safe choice.

## Motion

- Pivot swipe: content follows the finger 1:1, headers at about 0.5x. Settle with a standard ease-out, about 250ms.
- Page navigation: turnstile-style (the outgoing page tilts away, the incoming page tilts in), about 250ms. Fall back to a slide where Shell does not allow it.
- List items stagger in about 30ms apart on first load of a section. Not on every return.
- Implemented in `Controls/Motion.cs`: `FlyInAsync` (list items, pivot sections once on first view) and `TurnstileInAsync` (page elements flip in around their left edge, 40ms apart, run by `OneUiScaffold.EnterAsync`; pages that fill lists after loading set `ManualEntrance` and call it when done). Home items fly in every time a section arrives, from the side it came from (right when moving to a later section, left for an earlier one or when returning to the page); returning to a pushed page replays the turnstile mirrored. Each run claims its views, so an interrupted run can never leave an item offset. Only the first 14 items animate. Incoming pages only: Shell owns the outgoing animation.
- Respect the system "reduce motion" setting by disabling parallax and staggering.

## Expense category pages

- **Category settings** (`CategoryPage`): name, "amount required" toggle, subcategories (reorderable), position on the pivot, archive, and delete (move entries to another category, or delete them too). Reachable by long-pressing a category in the `expenses` list. With id 0 the same page creates a new category (the `+` on `expenses`). Reminder times live on the Reminders page, one row per category.
- **Entry form:** subcategory selector (chips), amount (required or optional by category), title, tags (free-typed with suggestions from tags used before, shared across categories), note, receipt photo.
- **Calendar and day pages** reuse the existing `CalendarView` and day-list layout, scoped to the category, restyled flat.

## Search

A pushed page (`SearchPage`), not an inline box: the `search` row heads the expenses list. Top to bottom: the query box, WHEN chips (any time, this month, last month, last 30 days, this year, pick dates, which reveals two date pickers), CATEGORY chips (tap again to clear), SHOW chips (everything / spent / got back), a one-line count and net total, then results grouped under date headings. Results update as you type or change a filter; tapping one opens that day.

## Accent on headings

Anything that names a section is `Primary`: the active pivot header (inactive ones stay `Muted`), the large page title, the small uppercase group labels (`SectionLabel` style, `Metro.Heading`, which also heads the date groups on search) and the 2px outline of secondary buttons. Body text, list lines and amounts stay `Ink`/`Muted`, so the accent only ever marks structure.

## Features vs your own data

Two kinds of rows share the expenses section, so they must never look alike. The app's tools (`search`, `insights`, `people`) are **tiles**: flat solid `Primary` squares in a row of three, `OnPrimary` text, a live figure top-left (entry count, this month's total, number of people) and the label bottom-left. The user's categories sit under a small `CATEGORIES` heading as plain text rows. Solid colour means "the app", plain type means "yours".

## Insights

A pushed page (`InsightsPage`). WHEN chips (same presets as search) and BY chips (category, subcategory, tag, person, month), then the range's total at 48sp light with a muted line (entries, average a day), then one bar per group: lowercase name left, amount right, a 10dp `Primary` bar on a `Faint` rail, and a caption with entry count and share. Months run chronologically, the rest by size; beyond 12 groups the tail folds into "N more". Tag bars show no share because an entry counts under each of its tags. Tapping a bar opens search filtered to its entries.

## Accessibility

- Minimum touch target 48dp, including header items and the `+` button.
- Contrast: `Ink` on `Paper` and `Muted` on `Paper` meet WCAG AA. Check `Muted` after any palette change.
- Every icon-only control has a `SemanticProperties.Description`.
- Swipe is never the only way: header taps and the `+` button give an alternative.

## Out of scope for now

- Line/trend charts and comparing two ranges. Insights has bar breakdowns only.
- Inline home-section content beyond the lists described above.
- Tablet / landscape-specific pivot layouts. Phone portrait comes first, landscape just stretches.
