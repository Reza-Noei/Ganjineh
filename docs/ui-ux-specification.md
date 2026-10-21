# UI/UX Specification — Ganjineh (Persian-first RTL)

Status: **Phase 0 — Proposed** | WPF on .NET 10, Fluent-inspired, RTL-native.

## 1. Design Principles

1. **Secrets are displayed rarely, on purpose, briefly** — hidden by default, explicit reveal,
   auto re-hide, clipboard that cleans up after itself.
2. **RTL is native, not mirrored at the end** — layout, animation directions, icon semantics
   (chevrons, progress) all authored right-to-left; numbers, URLs and Latin identifiers keep LTR runs.
3. **Calm density** — generous spacing, one primary action per screen, no wizard sprawl.
4. **Honest security UI** — warnings state real limits (no "military-grade" language; recovery
   power stated plainly).

## 2. Visual Language

- **Typography:** Vazirmatn (OFL) as UI font, weights Regular/Medium/SemiBold; Latin fallback
  Segoe UI Variable. Scale: 12 / 14 (body) / 16 (subtitle) / 20 / 28 (title). Tabular figures
  for codes and counters.
- **Iconography:** Lucide outline icons (ISC license), converted to XAML `PathGeometry` resources,
  16/20/24 px grid, `1.5` stroke. Directional icons mirrored for RTL via theme transform list.
- **Spacing:** 4-px base grid; card padding 16; list row height 44; section gap 24.
- **Corner radius:** cards 8, buttons 6, inputs 6; focus: 2-px accent ring (visible, RTL-aware).
- **Elevation:** flat surfaces + 1-px hairline borders (Fluent-ish, no heavy shadows).

### Theme tokens (Light / Dark)

| Token | Light | Dark |
|-------|-------|------|
| `Bg.Base` | #F5F5F7 | #1C1C1F |
| `Bg.Card` | #FFFFFF | #26262B |
| `Bg.Sunken` | #ECECEF | #151518 |
| `Text.Primary` | #1A1A1E | #F2F2F5 |
| `Text.Secondary` | #5A5A66 | #A8A8B4 |
| `Accent` | #2F6FED | #6E9BFF |
| `Accent.Soft` | #E3ECFF | #1F335C |
| `Danger` | #C42B1C | #FF99A4 |
| `Success` | #0F7B3E | #6CCB5F |
| `Warning` | #9A6700 | #E2B93B |
| `Divider` | #E3E3E8 | #3A3A42 |

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
