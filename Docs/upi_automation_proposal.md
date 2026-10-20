# Proposal: UPI and automated entry

Status: for discussion, nothing decided. Extends "Automating entry" in [future_scopes.md](future_scopes.md).

## Why

Most payments are UPI, so most expense entry is retyping something the phone has already been told. UPI has no public API for personal apps, so everything here uses what Android exposes (SMS, notifications, intents, files).

## Principle

Captured payments never write straight into `Expense`. They land in an **Inbox** of drafts (category guessed) that the user accepts or dismisses.

- `CreatedTicks` is set only on accept, so backup merge is unaffected.
- Dedup by the UPI reference, not by time or amount.
- All parsing is on-device; nothing leaves the phone.

## A. UPI to DayLogger (capture)

| Source | Gives | Notes |
|---|---|---|
| Bank debit/credit SMS | Amount, direction, account, date, **UPI ref (RRN)**, usually the payee VPA, often no name | Primary. Same for every UPI app. Needs `RECEIVE_SMS`/`READ_SMS` (fine for a sideloaded personal app, restricted on Play). |
| GPay / PhonePe / Paytm notifications | **Payee name**, cashback, "received from" | Notification-listener permission, filtered by package. Wording changes per app version. Enrichment only. |
| Statement export (PhonePe, Google Takeout, bank CSV/PDF) | Full history | Backfill and monthly reconcile. Manual. |
| Account Aggregator | Consented bank feed | Out of scope: needs a regulated entity. |
| Accessibility screen scraping | Anything on screen | Rejected: fragile and invasive. |

Merge SMS and notification for the same payment on amount within a short time window: SMS supplies the ref, notification supplies the name.

### Data changes
- `Expense.UpiRef` (nullable text). A plain added column, so `CreateTableAsync` applies it. Also in the backup DTO, `CreateAsync` and `RestoreAsync`.
- Merchant rules table: payee VPA/name pattern to category, subcategory, person, tags. Learns from corrections; known merchants may auto-accept. Goes in the backup too.
- Inbox drafts table (source, raw text hash, parsed fields, status).
- Credit SMS become payback candidates, matched to open spends via `ExpectBack` and `SettlesTicks`.

## B. DayLogger to UPI (launch and prefill)

Android deep link: `upi://pay?pa=<vpa>&pn=<name>&am=<amount>&tn=<note>&cu=INR`, opened through the system chooser.

1. `Person.Vpa` plus a **Pay** button: prefilled payment, and on return the entry is logged with category, person and note already set.
2. **QR scanner** that parses `upi://` codes: fills merchant name, creates Person + VPA, and uses the MCC (`mc=`) as a category hint.
3. **"Pay now" action** on recurring/reminder notifications (rent, EMIs, subscriptions).

Risk: UPI apps restrict intents. Payments to individual VPAs from a non-merchant app are often declined ("security reasons") or hit limits, mostly in GPay, and the result callback differs per app. Treat the callback as a hint and confirm through the SMS that follows. Needs a test on the user's own phone and apps before committing.

## Suggested order

1. Bank SMS parser into the Inbox, `UpiRef` dedup, merchant rules.
2. Notification listener to enrich with payee names.
3. `Person.Vpa`, QR scanner, Pay button (after testing the intent behaviour).

## Open questions

- Which bank(s) and which UPI app(s) are used most? Real SMS samples (account digits and names blanked) are needed to write the parser.
- Is `READ_SMS` acceptable, or should capture rely on notifications only?
- Auto-accept for known merchants: on by default, or opt-in per merchant?
- Backfill from a statement export: worth doing at the start?
- Does the Pay button matter, or is capture alone enough?
- Where does the Inbox live in the pivot: a row on `expenses` (like `people` and `search`), or a badge on `+`?
