# Phase 9.0 Test Results

Date: 2026-10-30 · Build: 0 warnings (`TreatWarningsAsErrors`) · **268/268 tests green**

| Suite | Result |
|-------|--------|
| Domain.Tests | 5/5 |
| Application.Tests (incl. 35 import tests) | 35/35 |
| Cryptography.Tests | 104/104 |
| IntegrationTests (incl. 13 Phase 9 tests) | 107/107 |
| Persistence.Tests | 4/4 |
| SecurityTests | 7/7 |
| Desktop.UITests (incl. 4 Phase 9 tests) | 6/6 |

## Unit — CSV pipeline (Application.Tests/BrowserImportTests, 35)

Schema detection (Chrome/Firefox/unknown/marker disambiguation), reordered headers, missing
optional columns (+ unsupported-column reporting), Firefox guid/PRTime mapping, no-invented-
fields, quoted commas + escaped quotes + multiline fields, UTF-8 BOM, CRLF and LF, duplicate
headers → first + reported, unterminated quote rejected, oversized file rejected, missing file
rejected, invalid UTF-8 rejected, empty rows counted, empty password importable, invalid URL
schemes rejected per row, bare-host URLs accepted, oversized titles rejected per row,
duplicates-within-file marked, origin-key theory tests (www-fold, subdomain, scheme, port,
path), Persian username folding, query-stripped origin display, source-file never modified +
no temp files created, dispose clears secret references.

## Integration — vault E2E (IntegrationTests/Phase9Tests, 13)

Chrome round-trip on a real SQLCipher vault (fields, provenance, no plaintext at rest,
lock→unlock persistence), Firefox guid/timestamp preservation, duplicate default-Skip never
overwrites, Update-Existing merge (password refresh, TOTP/custom fields/notes/favorite
preserved, history version appended), empty-CSV-password keeps stored password, Create Copy,
non-duplicate matrix (different username / subdomain / scheme; www duplicates), cross-vault
isolation (no flags, no cross-vault reads), per-record failure keeps others + exact row
reported, cancellation stops with exact prefix counts, locked session rejects parse+execute,
wrong-browser entry reported (never parsed), source file never modified.

## Wizard journey (UITests/BrowserImportViewModelJourneyTests, 3)

Full journey through the real wizard view-model on a real vault: entry preconfig, malformed
file aborts with meaningful error, clean parse → preview counts, duplicate default-Skip +
conflict combo, filter/select-all/deselect-all, update+import execution (`Imported: 1 |
Updated: 1`), history preserved, navigation event fires with the first written item,
wizard closes itself; vault loss closes the wizard via SessionChanged and blocks further
parses (session-lost error); wrong-format file produces an error without raw content.

## WPF shell E2E (UITests/Phase9UiTests, FlaUI/UIA3)

Settings → Import & Export → both browser wizards (shared wizard, preconfigured), step
navigation, dismissal, dark-theme swap while the wizard is open, Browse raises the shell
dialog handler and the Win32 open dialog appears.

## Environment limitation (recorded honestly)

The FlaUI journey could not be driven through the **modal Win32 open dialog** on this host:
its UIA provider times out on element calls, and neither BM_CLICK nor WM_COMMAND(IDOK)
confirms the Vista-style dialog programmatically; physical input also cannot be taken from
the user's foreground session. Coverage was therefore split: the shell up to the dialog stays
in FlaUI; everything past the dialog (preview/conflicts/results) runs at the wizard-viewmodel
boundary against the real services and real vault — the same objects the XAML binds. The
manual verification path for the dialog interaction is: run the app, Settings → Import from
Chrome → continue → browse → pick a real Chrome export → preview → import → item opens.
(Keyboard navigation and RTL of the wizard overlay are exercised by the shell's existing
flows; Esc closes the wizard through the same `TryCloseTopOverlay` chain used for all
overlays.)

## Bugs found & fixed during testing

1. **Wizard could not run twice** — `_importCts` was never cleared after a run, so a second
   parse silently no-op'd (user-visible; caught by the journey test).
2. **Import/UI-thread race** — importing from a thread-pool task could race the vault lock
   mid-insert; imports now run on the dispatcher (serialized with lock/switch, responsive
   between records).
3. **Faulted import left the wizard spinning** — unexpected exceptions in the run task now
   surface as a stopped-early result instead of an endless progress bar.
4. **UI tests raced over the real `%APPDATA%` files** — FlaUI test collections now run
   sequentially (`xunit.runner.json`).
5. PRTime conversion verified against fixtures (µs → s); Sylvan exception types translated to
   row-attributed import errors; UTF-16/UTF-8 BOM matrix verified.
