# Pro idea: AI receipt scan

Status: brainstorm only, nothing implemented. Open decisions are listed at the bottom.

## Idea

Inside an expense category the user taps **Scan**, photographs a bill, and the app drafts a new expense: title, description, subcategory and tags, plus the amount and date. The bill image is attached to the entry as its receipt.

## Privacy tension

DayLogger promises that nothing leaves the device unless the user exports a backup. A good scan needs a vision model, which breaks that promise, so the feature must be **opt-in**, clearly labelled ("the receipt image is sent to <provider>"), and off by default.

## Approaches

| Option | Quality | Privacy | Cost / effort |
|---|---|---|---|
| A. On-device OCR + heuristics (ML Kit Text Recognition / Document Scanner) | Reads text well; weak at title, tags, description | Fully local | Free; Android needs a binding, iOS would need Vision |
| B. Cloud vision LLM (e.g. Claude Haiku 5.5) | Best; handles messy, multilingual, handwritten receipts and picks sensible tags | Image leaves the device | A fraction of a cent per receipt |
| C. On-device LLM (Gemini Nano / AICore) | Medium | Local | Only a few Android phones; not dependable yet |

Plan: start with **B behind an opt-in**, add **A** later as an offline fallback or fully local mode.

## Pipeline (option B)

1. **Capture**: Scan button in the category's add-entry flow. Start with `MediaPicker.CapturePhotoAsync`; later the ML Kit document scanner for auto-crop and perspective correction.
2. **Save the image first** under `receipts/` (file name only, as today). If the AI step fails the bill is still attached.
3. **Downscale** to ~1500px long edge, JPEG ~80%.
4. **Call the model** with the image plus the category's context: its name, its subcategories and the existing tag list. Ask for strict JSON:
   `title, amount, currency, date, description, subcategory (existing or null), tags[] (prefer existing, lower-case, max 3), confidence`.
   Passing existing subcategories and tags prevents sprawl (`grocery` / `groceries` / `food`).
5. **Prefill, don't auto-save**: open the add-entry form with the fields filled in and low-confidence fields highlighted. The user confirms with one tap.

## Where it fits in the code

- `Services/ReceiptScanService` (new): builds the request, plain `HttpClient`, parses JSON into a `ScanResult` DTO. No SDK needed.
- Entry form (`expenseday?cat=&date=&add=1`): Scan button that fills the fields. `Amount` 0 already means "none" and the category's "amount required" rule still applies.
- Tags: the model returns strings; the existing lower-casing and tag-remembering logic handles them.
- Settings section: "AI scan" row with enable toggle, key/endpoint and the privacy note.
- Android manifest: `CAMERA` and `INTERNET` if not already present.
- Backup: the API key must never be written into a `.dlbackup`.

## API key

- **BYOK**: user pastes their own key in Settings, stored in `SecureStorage`. No server; fine for a personal app.
- **Proxy** (Cloudflare Worker / Azure Function) holds the key and the app calls it. Needed if the app is ever distributed to other people.
- Never embed a key in the APK.

## Edge cases

- Multi-item receipts: use the total as the amount, main items in the description. Per-line-item splitting is a separate, larger feature.
- Ambiguous dates (03/04/26): fall back to the selected day and flag it.
- Currency: the app has no currency concept today; decide whether to ignore or store it.
- Failures (offline, timeout, bad JSON): keep the saved image, open a blank form. Scanning must never block adding an expense.
- Not a receipt: return low confidence and let the user decide.
- Sensitive content (card digits, names): state what is sent and never log images.

## Rough effort

| Phase | Scope | Size |
|---|---|---|
| 1 | Scan → camera → save image → model → prefilled form; BYOK; opt-in toggle | ~1-2 days |
| 2 | Document scanner with crop, confidence highlighting, retry/error UX | ~1 day |
| 3 | Offline OCR fallback and/or proxy server | 1-3 days, optional |

## Later possibilities

Category, subcategory and tags are separate columns, so the same model access could power analysis features: re-tagging old entries, monthly summaries, spending distribution.

## Open decisions

1. Is opt-in cloud acceptable, or is fully local a hard requirement?
2. Personal use only (BYOK) or distributed (proxy)?
3. Prefill for review (recommended) or save straight away?
4. Currency and per-item lines now, or later?
5. How does this relate to the Pro version: is scan the paid feature, and if so how is Pro unlocked?
