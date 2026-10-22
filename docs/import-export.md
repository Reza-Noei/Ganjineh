# Import/Export — as built (Phase 6)

## Import

- Formats: generic CSV (Title/Username/Password/Website/Notes columns, alias-tolerant headers)
  and Bitwarden JSON export subset (items with login{username,password,uris,totp}, notes,
  folder). KeePass CSV and 1Password exports: deferred pending verified fixtures (contract §11).
- Pipeline: parse (bounded: 10 MB, 50 000 records) → validate → preview (counts, first records,
  duplicates by normalized username+title) → conflict policy (Skip / CreateCopy) → import via
  the item service (envelope-encrypted under the target session).
- TOTP: `otpauth://` URIs in CSV/JSON are parsed and stored encrypted (Phase 5 engine).
- Unsupported columns/records are counted and reported, never silently dropped.
- Plaintext source files: the wizard warns pre-import and post-import that the source file
  remains sensitive; no plaintext copies are made; parsed records live in memory only.

## CSV safety

- RFC 4180 parsing (quoted fields, escaped quotes, CRLF); malformed rows are rejected with a
  row number, never crash.
- Export: fields are quoted and doubled per RFC 4180; cells beginning with `= + - @` (and tab/CR)
  are prefixed with `'` to neutralize spreadsheet formula injection.

## Export

- Encrypted: the same `.gvbak` backup format (no second container format invented).
- Plaintext CSV/JSON: requires an explicit typed confirmation step in the UI; exports only live
  (non-trashed) logins by default with category selection; UTF-8 without BOM; RFC 4180 escaping
  with formula-injection defense; the file is unencrypted — stated in the confirmation dialog
  and completion summary.
