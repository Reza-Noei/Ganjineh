# Browser Password Import (Phase 9)

Import credentials from Google Chrome and Mozilla Firefox into the currently unlocked vault
via their documented **CSV export** files. CSV is only an import transport format — parsed rows
are mapped onto the standard Login item model and sealed by the regular vault services. There
is no browser-specific storage type, no plaintext cache, and no direct access to browser
credential databases (ADR-007 defers extension/native-messaging integration to later phases).

## Where

Settings → وارد کردن و برون‌بری (Import & Export):

- وارد کردن رمزها از Google Chrome
- وارد کردن رمزها از Mozilla Firefox

Both open the **same four-step wizard**, preconfigured for the chosen browser:

1. **Export instructions** — browser-specific steps for producing the CSV, with a prominent
   warning that the export file holds unencrypted passwords. Menu paths differ slightly between
   browser versions; the instructions describe the current stable UI and say so.
2. **File selection** — the standard Windows open dialog (CSV filter, existence checked). The
   file is opened read-shared, is never modified, and no temporary copies are made.
3. **Preview & conflict resolution** — every row with its website, username, source, import
   status, duplicate status and action. **Passwords are never displayed.** Rows can be
   selected/deselected (individually, all, none) and filtered.
4. **Import & results** — per-record progress and exact final counts (imported / updated /
   skipped / failed), a navigation button to the first written item, and the source-CSV
   cleanup warning with an "open containing folder" offer.

## Duplicate handling

Detection key = normalized origin + normalized username, matched against the **live logins of
the currently unlocked vault only**:

- Origin keeps scheme (http ≠ https), full host (IDN-folded; `www.` folded away, other
  subdomains preserved) and non-default port. Paths/queries are excluded from *matching* —
  stored URLs always keep them.
- Usernames fold Arabic/Persian character variants exactly like search (SearchNormalizer).
- Two accounts on the same domain with different usernames are never considered duplicates.

Conflict actions for duplicates: **Skip (default) / Update existing / Create copy**. Nothing is
overwritten without explicit user approval. Update-existing merges: password and username are
refreshed (an empty CSV password never blanks the stored one), the URL is added to the item's
URL list, the note is appended below existing notes, and TOTP secrets, custom fields, folder,
tags, favorite flag and the item's version history all survive. A duplicate on a second row of
the same file is also flagged (default Skip) so one export does not double-import itself.

## Provenance

Inside the encrypted payload Data dictionary (never the source path, never file contents):
`sourceBrowser` (`chrome`/`firefox`), `importedAt` (Unix seconds), `sourceRecordId` (the
Firefox export guid when present, otherwise the CSV row number), and for Firefox
`sourceTimePasswordChanged` (PRTime µs → Unix seconds). The vault item's own CreatedAt/UpdatedAt
remain vault-owned — external dates are carried as metadata only.

See [chrome-import.md](chrome-import.md) and [firefox-import.md](firefox-import.md) for the
supported schemas, and [import-security.md](import-security.md) for the threat model of the
pipeline itself.
