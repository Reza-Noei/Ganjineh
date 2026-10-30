# Phase 9.0 Implementation — Secure Chrome & Firefox Password Import

Status: **Complete** | 2026-10-30 | 268/268 tests green, 0-warning build.

## What was built

Browser password import from Google Chrome and Mozilla Firefox CSV exports into the currently
unlocked vault, through a shared four-step wizard. CSV is a transport format only — rows are
mapped onto the standard Login item model and sealed by the existing vault services. No
browser extensions, no native messaging, no direct browser database access (ADR-007 unchanged).

### Repository inspection findings (reported per spec §1)

1. **The Phase 6 import infrastructure documented in `docs/import-export.md` and the Phase 6
   commit message does not exist in the repository** — neither in history nor on disk. The
   documented "generic CSV + Bitwarden JSON import pipeline with conflict policy" was never
   committed (only backup/verify/restore exists, from `VaultBackupService`). Per the spec's
   instruction, the gap was reported and the browser-CSV import pipeline was built fresh on
   the existing architecture (`IVaultItemService`, `VaultSession`/generation guard,
   `ItemEncryption`, envelope format). Generic CSV/Bitwarden import and plaintext export
   remain unimplemented and are now documented as a gap (see import-export.md).
2. **`.gitignore` bug**: the `Backup*/` rule (line 266) silently ignores
   `src/PasswordManager.Application/Backup/` — `VaultBackupService.cs` and `BackupFormat.cs`
   have never been committed despite shipping since Phase 6. Fix: add a negation rule
   (`!src/PasswordManager.Application/Backup/**`) and commit the two files alongside this
   phase.

### Architecture (files created/modified)

**Application layer** (`src/PasswordManager.Application/Import/`):
- `BrowserImportModels.cs` — `ImportBrowser`, `ImportRowAction`, `ImportRecordStatus`,
  `ImportFileLimits` (10 MB / 50 000 records / field & record caps), password-free
  `ImportPreviewRow`, `ImportRowPlan`, secret-holding `BrowserImportSession : IDisposable`,
  `ImportRunResult` with per-record accuracy, provenance key constants.
- `BrowserCsvReader.cs` — bounded reader over Sylvan.Data.Csv 1.4.4: BOM detection
  (UTF-8 ± BOM, UTF-16 defensively), strict UTF-8 (invalid bytes reject the file), byte cap
  that throws instead of truncating, record-count/field-size caps, row-attributed malformed
  errors (Sylvan exceptions translated to `ImportFormatException` with row numbers).
- `BrowserCsvSchema` (same file) — header-based mapping (never positional): case-insensitive
  trimmed matching, aliases, duplicate headers → first occurrence + reported as unsupported,
  unknown columns reported as "not imported", Chrome-vs-Firefox marker disambiguation,
  wrong-file/wrong-entry detection (`ImportSchemaException`).
- `BrowserImportService.cs` — `ParseFile` (pure parse→map→validate), `MatchDuplicates`
  (vault-bound), `Parse` (both), `ExecuteAsync` (per-record transactional writes through
  `IVaultItemService` with session revalidation before every write, cancellation, per-record
  failure capture, truthful partial-run reporting). Row validation mirrors the credential
  model caps; URL policy: http(s) absolute or bare host accepted, everything else (incl.
  `javascript:`) marks the row invalid. `DuplicateKey` — origin keys keep scheme/host
  (IDN-folded, www-stripped, subdomain-preserving)/non-default port; paths & queries excluded
  from matching only; usernames fold Arabic/Persian variants via `SearchNormalizer`.
  Update-existing merges (never clobbers TOTP/custom fields/notes/favorite/folder/tags;
  empty CSV password never blanks the stored password; history appended via versioned update).
  Provenance (`sourceBrowser`, `importedAt`, `sourceRecordId`, Firefox
  `sourceTimePasswordChanged` as PRTime µs → Unix s) inside the encrypted payload.

**Desktop layer** (`src/PasswordManager.Desktop/`):
- `ViewModels/BrowserImportViewModel.cs` — shared four-step wizard VM: browser preconfig,
  instructions → file → preview (filter via `SearchNormalizer`, select all/none, per-row
  conflict combo for duplicates defaulting to Skip) → progress/results (counts, failure lines,
  navigation event, source-folder offer). Import session (password material) kept out of all
  bindable state; disposed on close/cancel/session loss. Session-loss closes the wizard.
  Execution runs on the WPF dispatcher so record writes are serialized with lock/switch (no
  mid-insert race), while awaits between records keep the shell responsive.
- `ViewModels/AppViewModel.cs` — `BrowserImport` child VM + `OpenImportWizardCommand`
  (closes Settings, opens the wizard preconfigured).
- `ViewModels/VaultWorkspaceViewModel.Redesign.cs` — `SelectItemByIdAsync` for post-import
  navigation.
- `MainWindow.xaml` — Settings → "Import & Export" section with the two Persian-labeled
  browser buttons (`GoogleChrome`/`Firefox` PackIcons, tooltips, AutomationIds); the
  app-level wizard overlay (RTL-safe flow-neutral layout, MD3 tokens, step panels, preview
  ListView with check/status/action cell templates, dark/light via DynamicResource tokens).
- `MainWindow.xaml.cs` — file-dialog ownership (CSV filter, existence check), navigation
  handler, open-source-folder handler (Explorer `/select` — the app never touches the file),
  Esc overlay chain extended.
- `App.xaml.cs` — DI: `IBrowserImportService`, `BrowserImportViewModel`.

**Localization** — 57 new fa/en key pairs (`Settings.Import*`, `Import.*`, `Error.SessionLost`),
exact parity (enforced by LocalizationParityTests), Persian-first wording, browser names kept
Latin.

### Design decisions & documented limitations

- **Parser choice**: spec §6 asked for a maintained parser; Sylvan.Data.Csv 1.4.4 chosen
  (zero transitive deps, MIT, actively maintained) and recorded in the supply-chain audit.
  Bounds and malformed-row policy stay in our audited layer.
- **Duplicate key**: origin+username with www-folding and scheme-awareness. Two accounts on
  one domain are never merged; paths/queries excluded from *matching* only (stored URLs keep
  everything) — documented in browser-import.md.
- **Firefox timestamps** are carried as provenance data keys, not as item dates — the domain
  model's CreatedAt/UpdatedAt stay vault-owned (§5 "do not invent values").
- **Titles**: browsers do not export titles universally; derivation order name → URL host →
  username → neutral `Imported <row>` label, because the domain requires a non-empty title.
- **Memory**: preview models have no password field (test-enforced). Session disposal drops
  references; managed strings cannot be zeroized — same accepted in-memory exposure as the
  existing R-1 threat-model position; no new plaintext persistence (verified: no temp files,
  source file untouched).
- **E2E split**: this host's modal-file-dialog UIA provider is unusable for automation
  (unresponsive provider; synthetic BM_CLICK/WM_COMMAND do not confirm the Vista dialog), and
  physical input cannot be stolen from the user's foreground. Therefore the FlaUI test covers
  the shell through Settings → wizard → browse → dialog-appears, and the complete four-step
  journey (parse → preview → conflicts → import → results → navigation → session-loss) runs
  through the real wizard view-model against a real SQLCipher vault in
  `BrowserImportViewModelJourneyTests`. This split is recorded in phase-9-0-test-results.md
  as an environment limitation, not a coverage gap in the app logic.

## Explicit boundaries respected

No Chrome/Firefox extension, no native messaging host, no direct browser credential database
access, no profile decryption, no background sync, no cloud/online APIs. No changes to vault
cryptography, envelope format, or database schema.
