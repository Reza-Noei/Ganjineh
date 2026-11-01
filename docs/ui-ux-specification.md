# UI/UX Specification — Ganjineh (Persian-first RTL)

Status: **Implemented** | WPF on .NET 10, Material Design (MaterialDesignThemes 5.3.2), RTL-native.

## 1. Design Principles

1. **Secrets are displayed rarely, on purpose, briefly** — hidden by default, explicit reveal,
   auto re-hide, clipboard that cleans up after itself.
2. **RTL is native, not mirrored at the end** — layout, animation directions, icon semantics
   (chevrons, progress) all authored right-to-left; numbers, URLs and Latin identifiers keep LTR runs.
3. **Calm density** — generous spacing, one primary action per screen, no wizard sprawl.
4. **Honest security UI** — warnings state real limits (no "military-grade" language; recovery
   power stated plainly).

## 2. Visual Language

- **Design system:** Material Design 3 via `MaterialDesignThemes` 5.3.2 (as of 2026-10-23,
  replacing the Phase-1 hand-rolled Fluent-inspired styles). Implicit control styles come from
  `Themes/MaterialDesign3.Defaults.xaml`; `Styles/Controls.xaml` rebases the Ganjineh styles
  (`IconButton` → Material flat, inputs → Material implicit TextBox/PasswordBox, destructive
  actions → `DangerButton`) on top. Light/Dark flips rebuild Material's `BundledTheme` at runtime
  (`ThemeService`), then the Ganjineh token dictionary below. The vendored Vazirmatn font also
  overrides Material's `MaterialDesignFont` resource, so Material chrome uses it too.
- **Typography:** Vazirmatn (OFL) as UI font, weights Regular/Medium/SemiBold; Latin fallback
  Segoe UI Variable. Scale: 12 / 14 (body) / 16 (subtitle) / 20 / 28 (title). Tabular figures
  for codes and counters. *As built (Phase 1): vendored at
  `src/Ganjineh.Desktop/Assets/Fonts` (Regular/Medium/SemiBold + OFL.txt), embedded as
  pack resources; missing-font fallback is automatic.*
- **Iconography:** Material Design Icons via `materialDesign:PackIcon` (compile-checked
  `PackIconKind` enum), 16/20/24 px. Directional icons mirror with `FlowDirection`.
- **Spacing:** 4-px base grid; card padding 16; list row height 44; section gap 24. RTL spacing
  rule: physical margins do not mirror with `FlowDirection`, so in stretch+auto column pairs the
  stretching element carries symmetric horizontal margins, and auto siblings use symmetric
  `4,0`-style margins — gaps stay identical in RTL and LTR (no touching/overlapping elements).
- **Corner radius:** cards 12, Material buttons/inputs per MD3; focus: Material ripple + focus ring.
- **Elevation:** Material cards with 1-px hairline borders (tone cards, no heavy shadows —
  keeps the "calm density" principle).

### Theme tokens (Light / Dark)

Ganjineh brand tokens, mapped to the Material palette; consumed via DynamicResource and swapped
together with Material's `BundledTheme` on theme change. Plus an `Overlay.Scrim` token
(Light #66000000 / Dark #99000000) for modal overlays.

| Token | Light | Dark | Material source |
|-------|-------|------|-----------------|
| `Bg.Base` | #F5F5F5 | #121212 | Grey 100 / dark surface |
| `Bg.Card` | #FFFFFF | #1E1E1E | surface / elevated surface |
| `Bg.Sunken` | #EEEEEE | #161616 | Grey 200 |
| `Text.Primary` | #212121 | #F5F5F5 | Grey 900 / Grey 50 |
| `Text.Secondary` | #757575 | #BDBDBD | Grey 600 / Grey 400 |
| `Accent` | #1976D2 | #2196F3 | Blue 700 / Blue 500 |
| `Accent.Soft` | #E3F2FD | #0D2B45 | Blue 50 |
| `Danger` | #D32F2F | #E57373 | Red 700 / Red 300 |
| `Success` | #388E3C | #81C784 | Green 700 / Green 300 |
| `Warning` | #FF6F00 | #FFB74D | Amber 900 / Orange 300 |
| `Divider` | #E0E0E0 | #424242 | Grey 300 / Grey 800 |

Text contrast ≥ 4.5:1 in both themes (checked in Phase 1); accent used for interactive elements,
never for body text on tinted backgrounds.

## 3. Shell Layout (three-column, RTL)

```
┌────────────────────────────────────────────────────────────────────┐
│ [toolbar:  vault name ▾   🔍 جستجو…                    ⚙  🔒 主题] │  ← top bar (RTL)
├──────────────┬────────────────────────────┬────────────────────────┤
│ ناوبری (راست)│  فهرست آیتم‌ها (وسط)        │  جزئیات (چپ)           │
│ همه آیتم‌ها   │  sort ▾  filter ▾          │  عنوان، آیکون نوع       │
│ علاقه‌مندی‌ها  │  ┌────────────────────┐    │  فیلدها (مخفی/آشکار)    │
│ پوشه‌ها       │  │ ورود — بانک ملی     │    │  [کپی] [نمایش] per field│
│ برچسب‌ها      │  │ ورود — ایمیل کاری ✓ │    │  یادداشت امن            │
│ سطل زباله     │  │ کارت — …           │    │  تاریخچه | پیوست‌ها      │
│ ──────────   │  └────────────────────┘    │  [ویرایش] [حذف]         │
│ تولید رمز     │  (virtualized rows)        │                        │
│ مرکز امنیت    │                            │                        │
│ پشتیبان‌گیری  │                            │                        │
│ تنظیمات      │                            │                        │
└──────────────┴────────────────────────────┴────────────────────────┘
```

- Nav (right, 240 px), list (center, fluid, min 320 px), details (left, 380 px); collapsible nav
  at <1100 px width with overlay behavior.
- Item rows: type icon, title (decrypted in-memory), subtitle (username masked), favorite star,
  TOTP mini-countdown when configured. Selection is single, keyboard-driven.

## 4. Screens

| # | Screen | Purpose / key points |
|---|--------|----------------------|
| 1 | **First-run onboarding** | 3 steps: welcome + security promise ("offline, yours only") → tour of vault concept → create-or-open. No account, no email, ever. |
| 2 | **Vault creation** | Name (stored encrypted), master password + strength meter + confirmation, KDF note ("unlock takes ~1 s by design"), folder location (installer default / portable Data). |
| 3 | **Recovery Key setup** | Full-screen reveal, copy/download, then 4-group confirmation challenge (recovery-design §4.1); skip impossible in v1; warning text per §5 of recovery-design. |
| 4 | **Vault picker** | Cards per vault: local label or file name, last opened, lock state; "open with Recovery Key" link; add/create. No decrypted names pre-unlock. |
| 5 | **Master Password unlock** | Password box, CapsLock/keyboard-layout warning (fa↔en layout is a top real-world failure), attempt counter, link to Recovery unlock. |
| 6 | **Recovery unlock** | Grouped input matching PMRK1 format, checksum-validated inline before submit. |
| 7 | **Main dashboard** | The §3 shell; lands on "همه آیتم‌ها". |
| 8 | **Item editor** | Type-templated forms; custom fields add/remove; reveal fields are `PasswordBox`-style; save creates a history version; unsaved-changes guard. |
| 9 | **Password Generator** | Live preview + regenerate; mode toggle رمز/عبارت; all FR-PG options; "use in field" flow; strength bar with plain-language verdict. |
| 10 | **Security Center** | Cards per issue class with counts → filtered lists; health summary; explicit note: تحلیل کاملاً آفلاین است. |
| 11 | **Backup & Restore** | Backup now / schedule / retention; restore file picker with pre-restore summary (vault ID, date, item count) and typed confirmation. |
| 12 | **Import / Export** | Source picker → column mapping → dry-run preview (counts, conflicts) → apply; export tab with encrypted vs. plaintext (typed confirmation) paths. |
| 13 | **Settings** | General (language fa/en, theme, Persian calendar toggle), Security (autolock, clipboard TTL, KDF params display + re-tune, recovery rotate), Vault (rename, MP change, delete with typed confirmation), Portable (data dir info, move wizard). |

Global states every screen must define: empty (first item / empty folder / empty trash), loading
(KDF/unlock progress with cancel? — no: cancellation mid-KDF is allowed and safe), error
(inline + toast), locked overlay (shell blurs and clears bindings).

## 5. Secret Display & Clipboard Rules

- Hidden by default: `••••••••`; reveal = explicit button **or** hold-to-reveal; auto re-hide
  after 30 s or on focus loss/window deactivate (blur + clear, not just visual).
- Copy buttons per secret → clipboard set with expiry = vault setting (default 60 s, 10–120);
  clear only if the app still owns the clipboard; on lock/exit best-effort clear.
- Windows clipboard history (Win+V) risk is disclosed in Settings with guidance to disable it —
  the app cannot control the OS feature (threat R-8).
- TOTP codes auto-refresh; copy never auto-triggers.

## 6. Localization & Bidi

- Neutral culture = **fa**; `en` satellite; runtime switch without restart (DynamicResource
  string table + re-measure).
- All strings via resource keys; **zero** literal UI strings in code/XAML (enforced by review
  checklist + optional analyzer).
- Mixed-direction: user names/URLs/emails rendered with LTR isolate (`U+2066…U+2069`) inside
  RTL text; file paths and codes in `Style="{StaticResource LtrRun}"`. Numbers use
  `arabicDigits="context"` preference (Western digits default, Persian digits display toggle).
- Dates: Gregorian internally; display default **Persian (Solar Hijri)** calendar, switchable.

## 7. Accessibility & Keyboard

- Full keyboard path: `Tab` order RTL-consistent; `Ctrl+F` search, `Ctrl+N` new item,
  `Ctrl+G` generator, `Ctrl+L` lock, `F2` edit, `Del` trash, `Ctrl+C` copies selected field.
- Reveal actions reachable without mouse; screen-reader names on all icon-only buttons;
  contrast AA; focus never lost inside overlays; `Esc` closes overlay/cancels reveal.

## 8. Motion

Minimal: 150–200 ms fades/slides; progress shown for KDF (deterministic-ish indeterminate bar
with "این تأخیر عمدی است" caption); no animation on secret reveal (content swap is instant to
avoid staged-exposure artifacts).
