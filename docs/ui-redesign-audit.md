# UI Redesign Audit & Screen Plan — Ganjineh (2026-10-23)

Scope: the Material-Design-3 redesign brief (audit → tokens → shell → screens → visual
verification). Baseline: the 2026-10-23 Material adoption pass (tokens, themes, RTL margin
rules, MD3 control rebase) already in the working tree.

## 1. Audit findings

### Shell / navigation
- **S1 — Dead nav placeholders.** The sidebar "Tools" group renders Generator, Security Center,
  Backup, Settings as `IsEnabled="False"` buttons with a hard-coded Persian `ToolTip="بعداً"`.
  Generator and Security Center are in fact functional (top bar); Backup and Settings have no UI
  anywhere. Violates "no placeholder controls" and "no literal UI strings".
- **S2 — No active-section feedback.** `SetSectionCommand` swaps list content but no nav entry is
  visually selected; Security Center is reachable only from the top bar.
- **S3 — No vault selector in the sidebar** (spec §3); vault switching only via the top-bar
  "switch vault" (which is just lock → picker).
- **S4 — Theme and language toggles unreachable.** `Top.ToggleTheme`, `Top.ToggleLanguage`,
  `Menu.Theme.*`, `Theme.Status.*` keys exist; `ThemeService`/`LocalizationService` support live
  flips; no control anywhere issues them.
- **S5 — Top bar hierarchy**: four text+icon buttons of equal weight; Lock should be the single
  emphasized action.
- **S6 — No keyboard shortcuts** (spec §7: Ctrl+F/N/G/L).

### Item list (center)
- **L1 — Type-blind rows.** Every row renders `PackIcon Kind="Key"` regardless of
  `CredentialRowViewModel.Kind`. `ItemFormRegistry` glyph table is emoji-based and unused.
- **L2 — Favorite state invisible** in rows (`Favorite` exists on the row VM).
- **L3 — Static header** `Center.ListHeader` — does not reflect section/folder/tag/trash.
- **L4 — No sort UI** (`SortBy` observable + `ItemFilter.SortBy` fully supported in the VM).
- **L5 — No search placeholder** (`Search.Placeholder` key defined, never used).
- **L6 — Security Center under-designed**: 4 of 7 finding kinds surfaced
  (Duplicate/Empty/Old computed but unbound), `FilterFindings` command exists with no filter UI,
  finding rows are bare title buttons whose tooltip shows the raw `Kind` enum + `DetailKey`
  (unlocalized), no "analysis is fully offline" note (spec §4.10), score is plain text.

### Details pane
- **D1 — Copy/reveal buttons are icon+text flats**; three per secret dominate the pane. Spec
  wants compact per-field actions.
- **D2 — TOTP copy button reuses `Details.CopyPassword`** ("کپی رمز") — wrong label for a code.
- **D3 — Username shown twice** (subtitle under the title and again as the Username field).
- **D4 — No LTR isolation** for passwords/URLs/emails/technical values in RTL flow (spec §6).
- **D5 — History section always visible** with a redundant "Load" button (versions already load
  in `LoadDetailAsync`) and an always-enabled "Restore" that no-ops without selection.
- **D6 — Attachments gated on `FalseToVisible(IsLoginDetail)`** — shown only for non-login
  items, while the attachment service is type-agnostic (`AddAttachmentFromFileAsync` works for
  any selection). Read as an inverted gate: attachments belong to all types.
- **D7 — Actions float mid-scroll** (Edit/Delete/Favorite inside the scrolling stack); on long
  items they scroll out of view.

### Editor
- **E1 — No item-type picker.** `EditorKindChoices` / `EditorKind` exist; the XAML never binds
  them. Non-login items are only creatable by first navigating to a type category. No
  `OnEditorKindChanged` rebuild exists, so a live picker also needs a rebuild command.
- **E2 — No folder selector** (`EditorFolderId`, `Editor.Folder` key unused).
- **E3 — "Keep existing password" hint shown when creating** (only meaningful when editing).
- **E4 — Flat wall of fields** — no grouping (item / security / organization); custom-field row
  is cramped (90 px type combo); no dialog header icon; no Esc affordance.

### Vault lifecycle screens
- **V1 — Picker**: mixed button hierarchy (raised-with-icon, accent flat, plain flat); no vault
  identity mark. Otherwise sound.
- **V2 — Create**: master-password strength is text-only (`StrengthHint`); no meter. Recovery
  stage is solid (card + copy + challenge).
- **V3 — Unlock**: no identity mark; functionally complete (password/recovery toggle, Enter
  submit, error/busy states).

### Missing surfaces
- **M1 — Settings** (language fa/en, theme, clipboard TTL 10–120 s, auto-lock minutes 0=off):
  all backed by `AppSettings` + `LocalizationService` + `ThemeService` + `AutoLockService`
  (reads settings live) — only the UI is missing.
- **M2 — Backup/Restore**: `IVaultBackupService` (create/verify/restore/selective/prune,
  `.gvbak` v1) fully implemented and tested — **not registered in DI**, no UI.
- **M3 — Import/Export**: no backend exists in the codebase (roadmap Phase 6 text overstates).
  No UI can honestly surface it; documented as a gap, not faked.
- **M4 — `EmptyTrashAsync` runs destructively with no confirmation.**

### Non-goals this pass
- Import/Export UI (no backend — M3).
- Collapsible nav < 1100 px, Persian calendar display, caps-lock warning on unlock (spec items;
  deferred, listed in "Remaining issues").
- Selective restore UI (advanced; service API exists).

## 2. Screen-by-screen redesign plan

| Screen | Changes |
|--------|---------|
| Design system | `ItemType→PackIconKind` mapper; `LtrValueText` (Consolas + LTR) for secrets/URLs/emails; nav-active style (`SectionEquals` converter); filter chips; stat cards; field-label/value pair styles; watermark search hint. |
| Shell | Sidebar rebuilt: vault block (name + switch/lock, keeping `SwitchVaultButton`/`LockVaultButton` IDs), live nav for All/Favorites/types/folders/tags, Tools → Generator (overlay), Security Center (section), Backup (overlay), Trash; Settings (overlay). Active-section highlighting. Top bar slims to brand + theme/language toggles + Lock. Ctrl+F/N/G/L shortcuts. |
| Item list | Per-type icon avatars, favorite stars, contextual section header, sort combo, search watermark, empty state kept. |
| Security Center | Replaces list + details columns (span trigger); score bar, all 7 stat chips double as filters (`FilterFindings`), localized finding rows (icon + kind + title + detail), offline note, refresh kept. |
| Details | Header block (type avatar, title, subtitle, favorite star toggle); grouped field cards (credentials/TOTP/fields/notes/attachments/history); icon-only copy/reveal with localized tooltips; `LtrValueText` on technical values; TOTP gets its own copy label; footer action bar pinned (Edit/Favorite/Delete; trash actions in trash view); attachments for all types; history auto-loaded (Load button removed, Restore selection-gated). |
| Editor | Header icon + title; type picker (create mode only) with dynamic-field rebuild; folder picker; grouped sections; password-keep hint only when editing; wider dialog; all IDs kept. |
| Generator | Preset chips (`ApplyPreset`), labeled excluded-characters input, inline output copy. |
| Vault lifecycle | Picker: consistent button hierarchy + shield mark; Create: strength hint kept (meter deferred); Unlock: mark + existing behavior. |
| Settings (new) | Overlay: language (fa/en), theme (Light/Dark), clipboard TTL slider (10–120 s), auto-lock presets (0=off,1,5,10,15,30); applies + persists immediately via `IAppSettingsStore`. |
| Backup/Restore (new) | Overlay: create backup (save dialog → `CreateAsync`), verify backup (open dialog → `VerifyAsync` cryptographic + summary), restore (file + destination + master password handoff → `RestoreAsync`). `IVaultBackupService` registered in DI. |
| Dialogs | Delete confirm kept; **empty-trash confirm added**; Esc closes overlays. |

## 3. Verification protocol

1. `dotnet build` 0 warnings; unit test suites green.
2. FlaUI E2E (`Phase3UiTests`) green — all automation IDs preserved.
3. `VisualReviewTests` harness: drives the real app through picker → create → recovery →
   workspace → details → editor → generator → security center → settings → backup → trash, in
   fa/light, fa/dark, en/light, and captures `CopyFromScreen` screenshots (per the PrintWindow
   mirroring gotcha) into `artifacts/ui-review/` for human + model inspection.
4. Defects found in screenshots are fixed and re-captured before completion.
