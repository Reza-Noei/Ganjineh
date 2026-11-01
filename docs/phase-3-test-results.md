# Phase 3 Test Results

Date: 2026-10-22 | Suite: `dotnet test Ganjineh.slnx` — **113/113 green**, zero warnings.

## Coverage by area

| Area | Tests | Highlights |
|------|-------|-----------|
| Domain / Application / Cryptography / Persistence (regressions) | 71 | all Phase 1–2 suites still green |
| Vault catalog | 3 | list+order, remove-from-list keeps files, delete removes files+entry, rename updates encrypted name + label with identity untouched |
| Credential CRUD | 4 | Unicode round-trip (Persian/emoji), update/delete, validation (empty title, overlong fields), locked/stale-session rejection |
| Vault isolation | 1 | per-vault item lists; cross-vault reads fail (AAD + identity) |
| Search | 2 | Persian variant folding (ي/ى/ك/alef/ZWNJ/diacritics); title/username/URL/notes match; password never searchable |
| At-rest (Phase 3 payload) | 1 | sentinel password/title absent from vault.db and WAL bytes |
| UI E2E (FlaUI/UIA3) | 1 | full flow: picker → create vault (name, password, recovery transcription challenge) → add Persian credential → search "ملی" (Arabic-Kaf spelling matches via folding) → details masked → copy password → lock → picker → re-select → unlock → workspace live |

## What the E2E proves end-to-end

The 22-second UIA run exercises the real product binary: RTL Persian UI rendering, guided
creation with the recovery-challenge gate, envelope-encrypted persistence through the real
SQLCipher database, debounced Persian search, password masking, clipboard copy, lock → picker →
unlock round-trip — all through AutomationIds, with settings/registry restored afterwards so the
run leaves no user-visible state.

## Known issues found and fixed during the phase

1. Recovery-challenge prompt rendered the literal `{0}` placeholder — fixed with a computed
   prompt (invariant digits) + regression coverage in the E2E.
2. `SearchBoxStyle` (TargetType=TextBox) applied to PasswordBox crashed template instantiation —
   dedicated `PasswordBoxStyle` added.
3. Registry API extended (`List/Remove/SetLabel/GetPath`) to serve the catalog without leaking
   the private entry type.

## Not yet tested (deferred with the features)

- Auto-lock timer behavior is implemented (`AutoLockService`) but not UI-tested (timer-based;
  covered indirectly by manual verification) — flap coverage planned with Phase 4 settings UI.
- Session-lock (Win+L) path is a thin `SystemEvents` forwarder; verified manually.
- Remove-from-list / delete-vault have service tests; their picker overlays are not UIA-driven yet.
