# Future scopes

Ideas for top-level features, not yet committed to.

## Priority

1. **Budgets**: monthly limit per category, with a warning near the limit. (Insights bars could show the limit as a marker.)
2. **Recurring entries**: auto-add rent, subscriptions and EMIs.
3. **More analysis**: trend lines across months per category, compare two ranges, income vs spend.

## Automating entry

Goal: cut data entry to one confirming tap for expenses and to nothing for most streaks. Ordered by value for effort; not committed.

**Principle:** captured expenses never go straight into `Expense`. They land in an **Inbox** of drafts (category already guessed) that the user confirms or dismisses. `CreatedTicks` is set only on accept, so backup merge stays correct. Dedup by a hash of the source (SMS id, or amount + merchant + time). All parsing stays on the device.

1. **Notification actions on reminders**: "Log 20" / "Done" / "Nothing today" buttons on the existing `ReminderService` notifications, so a log needs no app launch.
2. **Remembered defaults and templates**: last category, subcategory, person and tags per category; "repeat yesterday"; one-tap templates ("Tea 20"); title autocomplete fills the rest.
3. **Streak sources**: optional per-streak source, manual by default. Health Connect metric + threshold (steps, workout, sleep), app usage (Digital Wellbeing), geofence ("gym" after 30 min), or a calendar event title. End of day with no signal breaks the streak or asks "forgot?".
4. **Inbox with bank/UPI capture**: parse SMS or payment notifications (`READ_SMS` or a notification listener) into amount, merchant and direction (spend, refund, cashback). A merchant → category/subcategory/person/tags rules table learns from corrections; known merchants can auto-accept. Refunds are matched to the original spend through `SettlesTicks`. Needs parsers tuned to the user's bank formats and the rules table added to the backup DTOs.
5. **Other capture paths**: Share target for receipt photos/screenshots with on-device OCR (ML Kit), bank-statement CSV import, Quick Settings tile and long-press app shortcuts.

UPI-specific capture (bank SMS, UPI app notifications) and launching UPI apps from DayLogger are written up in [upi_automation_proposal.md](upi_automation_proposal.md).

Recurring entries (rent, subscriptions) and home widgets below belong to the same effort.

## Other ideas

- **Journal/notes**: a daily mood and note log.
- **Habit stats**: streak history, best streak, weekly/monthly completion.
- **Home widgets**: a quick-add widget and a quick-entry sheet widget exist. Still open: tick a streak, today's total, one-tap category/template buttons.
- **CSV/PDF export**: for spreadsheets and tax records.
- **App lock**: PIN or biometric.
- **Cloud backup**: optional auto-backup to Drive/OneDrive.
- **Multi-currency**: currency per entry or category.
- **Year in review**: a shareable summary card, extending `ShareCardRenderer`.
