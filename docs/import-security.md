# Import Security (Phase 9)

Threat model for the browser-CSV import pipeline: the CSV is **untrusted attacker-influenced
input** (it may come from a compromised or malicious browser, or be crafted), it contains
**plaintext credentials**, and it is processed on a machine whose vault is open.

## Processing envelope

- **One untrusted file, read-only**: opened with `FileShare.Read`, never modified, never
  renamed, never deleted by the app. No temporary copies are created at any point — parsing
  streams straight from the source into memory. Verified by tests (file bytes unchanged, no
  sibling files created).
- **No execution of content**: CSV values are stored as data. They are never interpreted as
  formulas, scripts or commands (the formula-injection defense applies to *export*; import
  stores values verbatim), and the app never makes network requests to imported URLs — opening
  a URL stays behind the existing `IsSafeWebUrl` http(s)-only guard in the UI.
- **Maintained parser**: RFC 4180 mechanics come from Sylvan.Data.Csv 1.4.4 (single package,
  no transitive dependencies, MIT). Quoted fields, embedded commas/newlines/quotes, CRLF/LF,
  BOMs and Unicode/Persian text are covered by the unit matrix. Our layer rejects what the
  parser accepts but we do not want: unterminated quotes (malformed), invalid UTF-8
  (encoding error), oversized files/records/fields (bounds).

## Bounds (import-security limits)

| Limit | Value | Failure mode |
|-------|-------|--------------|
| File size | 10 MB (matches Phase 6 bound) | whole file rejected |
| Record count | 50 000 | whole file rejected |
| Field length | 8 192 chars | whole file rejected (malformed) |
| Record buffer | ≥ 2× record cap (Sylvan `MaxBufferSize`) | unterminated quote cannot allocate without bound |
| Item field caps | title 200 / username 320 / password 1000 / URL 2048 / notes 10 000 | row marked invalid, shown with reason |

## Plaintext lifetime

- Passwords are held in `BrowserImportSession`'s private map — never in bindable view-model
  state. The preview table (`ImportPreviewRow` / `ImportRowViewModel`) **has no password
  field at all** (verified by test).
- Duplicate detection decrypts existing vault payloads only far enough to read
  username/URL/title; the JSON buffer is zeroized immediately and the stored password is never
  materialized.
- Disposing the session (wizard close, cancel, session loss) drops all secret references.
  Managed string contents cannot be reliably zeroized — the accepted in-memory exposure while
  the vault is unlocked is unchanged from the existing threat model (R-1,
  secure-memory-limitations.md); import adds no new persistence of plaintext.
- Errors and logs carry row numbers and resource keys only — never values. Verified by tests
  (wrong-format error must not contain file content; failure lines contain row + key).

## Vault isolation

- Import targets exactly the one explicitly unlocked `VaultSession` passed in. Every entry
  point asserts `State == Unlocked` (throws `StaleSessionException`), **revalidated before
  every write**; the wizard additionally subscribes to `IVaultSessionManager.SessionChanged`
  and closes itself (dropping the parsed session) on any lock or vault switch.
- Duplicate matching reads only the current session's database — never another vault's. A
  credential identical to one in a *different* vault imports as a fresh item (by design,
  verified by test).
- In the WPF app the wizard runs each record on the UI dispatcher, so record writes are
  serialized with lock/switch actions — a lock can land only between records, where the next
  record's session revalidation stops the run cleanly (§11 semantics: completed records stay,
  counts stay exact).

## Transactions and partial runs

Each record is written through the existing `IVaultItemService` (insert + first version, or
versioned update) in its own database transaction. A failed record (validation, missing
target) fails alone; a cancel or session loss keeps the exact completed prefix. The summary
reports imported/updated/skipped/failed individually and never rounds a partial run up to
success. Update-existing merges into the matched item (username, non-empty password, URL list
append, note appended below existing notes) and preserves TOTP keys, custom fields, folder,
tags, favorite and version history; an empty CSV password never blanks the stored one.

## Provenance

`sourceBrowser`, `importedAt`, `sourceRecordId` (and Firefox `sourceTimePasswordChanged`)
ride inside the encrypted payload — non-sensitive, no source file path, no file contents. The
original CSV is never copied, persisted, or referenced from the vault.
