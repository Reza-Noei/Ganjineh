# Import/Export — as built

> Correction (Phase 9, 2026-10-30): this page previously described a Phase 6 import/export
> pipeline (generic CSV + Bitwarden JSON import, plaintext CSV/JSON export) that **was never
> committed to the repository**. Only the encrypted backup/verify/restore half existed
> (`VaultBackupService`, .gvbak). The description below reflects what actually exists; the
> generic formats remain a documented gap for a future phase.

## Import (browser CSV — Phase 9)

- Formats: Google Chrome and Mozilla Firefox password exports (header-based mapping, schema
  per [chrome-import.md](chrome-import.md) / [firefox-import.md](firefox-import.md)).
- Pipeline: bounded parse (10 MB / 50 000 records) → validate → preview (passwords never
  displayed; duplicates flagged by normalized origin+username against the live vault only) →
  conflict policy per row (Skip — default for duplicates / Update Existing / Create Copy) →
  per-record transactional import via the item service (envelope-encrypted under the target
  session).
- Provenance (`sourceBrowser`, `importedAt`, `sourceRecordId`, Firefox
  `sourceTimePasswordChanged`) rides inside the encrypted payload. See
  [browser-import.md](browser-import.md) and [import-security.md](import-security.md).
- Unsupported columns/rows are counted and reported, never silently dropped.
- Plaintext source files: the wizard warns pre-import and post-import that the source file
  remains sensitive; no plaintext copies are made; parsed records live in memory only and are
  dropped when the wizard closes.

## Not implemented (gap, deferred)

- Generic CSV import (Title/Username/Password/Website/Notes) — the Phase 9 browser adapters
  cover the two browser schemas only.
- Bitwarden JSON import.
- Plaintext CSV/JSON export with formula-injection defense (the defense is specified in the
  Phase 0 design and applies when this is built).
- KeePass CSV / 1Password exports — deferred pending verified fixtures (contract §11).

## CSV safety

- Browser CSV parsing: RFC 4180 via Sylvan.Data.Csv with our bounded-error layer — malformed
  rows rejected with a row number, never crash (import-security.md).

## Encrypted backup/export

- `.gvbak` v1 (Phase 6, unchanged): authenticated manifest + chunked-AEAD package of the
  VACUUM INTO snapshot and attachment blobs; three-level verification (structural /
  cryptographic / rehearsal restore); full and selective restore with cross-vault
  re-encryption; recovery-key unlock; retention pruning — see the backup-*.md docs.
