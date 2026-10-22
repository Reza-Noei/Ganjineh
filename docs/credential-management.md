# Credential Management — as built (Phase 3)

## Domain shape

`CredentialDraft` (input) → `CredentialSummary` (list row, **no password**) → `CredentialDetail`
(full record, materialized only on selection). Payload = canonical JSON `{t,u,p,w,n}` sealed in a
PMENV1 envelope: `item_key`, key_domain=Item, AAD = VaultID‖itemUUID‖"payload.v1". Every save
appends an `item_versions` row (schema is Phase-4-ready; history UI is not exposed yet).

## Invariants and validation (`CredentialValidation`)

Title required (1–200); username ≤ 320; password ≤ 1000; URL ≤ 2048; notes ≤ 10 000; UTF-8 as
typed, no transformations. "Open website" only for absolute `http(s)` URLs (validated before
`Process.Start` — never shell-inject arbitrary strings).

## Persistence

- `items` (structural, plaintext-in-cipher-DB: id/type/favorite/trashed/timestamps) +
  `item_versions` (append-only envelope rows). One DB per vault; no cross-vault queries
  (enforced by the single-session model and AAD VaultID binding — tested).
- List path decrypts each payload but retains only title/username/URL — the password leaves the
  transient buffer immediately (minimize plaintext exposure, contract §5.2).
- All writes transactional (`BEGIN IMMEDIATE` … `COMMIT`); ids are UUIDs; timestamps UTC Unix seconds.
- Deletion is permanent DELETE + cascade (trash/restore = Phase 4). SQLCipher pages are freed, but
  previously written ciphertext may remain in SSD cells — no secure-erasure claim (R-6).

## UI flows

List rows show a fallback glyph, title, username. Details show masked password (`••••••••`,
Consolas) with explicit reveal/hide toggle, per-field copy buttons (clipboard-security.md), open-
in-browser, edit and delete (both with explicit overlays/confirmation). Dates shown localized;
passwords and URLs render LTR inside the RTL layout.
