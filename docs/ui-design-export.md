# Ganjineh UI Design Export — complete as-built reference

Self-contained description of the entire implemented desktop UI: tokens, styles, layout
geometry, every screen and overlay, behaviors, and automation IDs. Written so an agent (or a
human) can understand, reason about, or modify the UI **without reading the XAML first**.
**2026-11-01: rewritten for the premium redesign ("Calm Slate & Indigo")** — see
`ui-overhaul-audit.md` for the audit and rationale; this document describes **what exists now**.

Source-of-truth files (read these when this doc and the code disagree):

| File | Role |
|------|------|
| `src/PasswordManager.Desktop/MainWindow.xaml` (~2100 lines) | **All** pages and overlays — one `DataTemplate` per view-model |
| `src/PasswordManager.Desktop/MainWindow.xaml.cs` | Code-behind: keyboard shortcuts, file dialogs, PasswordBox handoff |
| `src/PasswordManager.Desktop/Themes/Light.xaml` / `Themes/Dark.xaml` | Brand color tokens (light/dark dictionaries, identical keys) |
| `src/PasswordManager.Desktop/Styles/Controls.xaml` | Ganjineh style layer on top of Material Design |
| `src/PasswordManager.Desktop/App.xaml` | Resource merge order, fonts, converters |
| `src/PasswordManager.Desktop/Services/ThemeService.cs` | Runtime light/dark rebuild (Material Indigo) |
| `src/PasswordManager.Desktop/Services/ItemIconMapper.cs` | Item type → icon table |
| `src/PasswordManager.Desktop/Localization/` | `LocExtension` markup + value converters |
| `src/PasswordManager.Infrastructure/Localization/Strings.resx` (fa, neutral) + `Strings.en.resx` | All UI strings; zero literal strings in XAML; fa/en key parity is test-enforced |
| `docs/ui-overhaul-audit.md` | 2026-11-01 redesign audit + art direction |
| `docs/ui-ux-specification.md` | Original design spec (principles, intent) |
| `docs/ui-redesign-audit.md` | 2026-10-23 audit (defect history — fixed) |

---

## 1. Platform and architecture model

- **WPF on .NET 10**, single window (`MainWindow`, AutomationId `MainWindow`), **1240×800
  default, min 1000×680**, `WindowStartupLocation="CenterScreen"`, canvas background `Bg.Base`.
- **Material Design 3** via **MaterialDesignThemes 5.3.2**, `BundledTheme` **Indigo/Indigo**
  (App.xaml + `ThemeService` constants — Material chrome follows the brand accent). Merge order:
  1. `materialDesign:BundledTheme` — replaced at runtime by `ThemeService` for dark mode
  2. Material's `MaterialDesign3.Defaults.xaml` — implicit control styles
  3. `Themes/Light.xaml` — brand tokens (swapped for `Dark.xaml` at runtime; **same keys**)
  4. `Styles/Controls.xaml` — Ganjineh styles, `BasedOn` the Material implicit styles
- **Single-window shell, page = DataTemplate.** `ContentControl Content="{Binding CurrentView}"`
  displays `VaultPickerViewModel`, `CreateVaultViewModel`, `UnlockViewModel`, or
  `VaultWorkspaceViewModel` by template type. No `Page`/`Frame` navigation.
- **Overlays, not dialogs.** Modals are `Grid` layers with `Overlay.Scrim` over the content,
  toggled by VM bools. `Esc` closes the top overlay. Workspace-scoped overlays (editor,
  generator, confirms) live inside the workspace template; app-level overlays (settings,
  backup, import wizard) sit at the window root.
- **FlowDirection is dynamic** — `RightToLeft` for Persian (default), `LeftToRight` for
  English, live-flipped. **The shell is authored RTL-first: the workspace's column 0 paints on
  the RIGHT.**
- MVVM with CommunityToolkit `[ObservableProperty]`. Strings via `{loc:Loc Key}` bound to
  `ILocalizer`, re-resolving live on language switch.
- **Deliberately not split into per-view files:** seven templates use code-behind `Click`
  handlers (PasswordBox handoff, dialogs), which cannot resolve from merged
  ResourceDictionaries; see `ui-overhaul-audit.md` §1 non-goals.

---

## 2. Color tokens — "Calm Slate & Indigo"

Cool slate structure + one indigo accent; semantic colors carry security meaning only.
Consumed via `DynamicResource`; **every key exists in both theme dictionaries** (parity is a
hard rule — add new keys to both together).

| Token | Light | Dark | Role |
|-------|-------|------|------|
| `Bg.Base` | `#F8FAFC` | `#0F172A` | canvas (window, list region, top bar) |
| `Bg.Card` | `#FFFFFF` | `#1E293B` | surface: sidebar, details, dialogs, search field |
| `Bg.Sunken` | `#F1F5F9` | `#131C2F` | wells (TOTP, outputs, recovery key), status bar |
| `Text.Primary` | `#1E293B` | `#F1F5F9` | primary text |
| `Text.Secondary` | `#64748B` | `#94A3B8` | captions, labels, quiet icons |
| `Text.Disabled` | `#94A3B8` | `#64748B` | paths, counts, micro text |
| `Accent` | `#4F46E5` | `#818CF8` | indigo — interactive elements, avatar glyphs, brand tile |
| `Accent.Soft` | `#EEF2FF` | `#2A2F55` | avatar fills, selected nav/rows, brand hero |
| `Danger` | `#DC2626` | `#F87171` | destructive buttons, errors, restore accent |
| `Danger.Soft` | `#FEF2F2` | `#2EF87171` | danger icon tiles, destructive hovers |
| `Success` | `#16A34A` | `#4ADE80` | success (verify-backup tile; reserved) |
| `Success.Soft` | `#F0FDF4` | `#2E4ADE80` | success tile |
| `Warning` | `#D97706` | `#FBBF24` | favorites, cautions, warning-severity findings |
| `Warning.Soft` | `#FFFBEB` | `#2EFBBF24` | caution tint |
| `Divider` | `#E2E8F0` | `#334155` | hairlines, panel separators |
| `Divider.Strong` | `#CBD5E1` | `#475569` | input borders |
| `Accent.Hover` | `#4338CA` | `#6366F1` | accent hover |
| `Item.Hover` | `#0F1E293B` | `#14FFFFFF` | row/nav hover wash |
| `Overlay.Scrim` | `#4D0B1220` | `#990B1220` | modal scrims (slate-tinted) |

One constant: white `#FFFFFF` glyphs on `Accent` tiles (brand). Dark mode is designed on
slate-navy, not inverted. Text contrast ≥ 4.5:1 both themes; `Accent` never for body text on
tinted backgrounds.

---

## 3. Typography

- **Vazirmatn** (OFL; Regular/Medium/SemiBold), vendored at `Assets/Fonts`, embedded as pack
  resources; exposed as `App.FontFamily` **and** `MaterialDesignFont` (Material chrome uses it).
- **No letter-spacing, no uppercase styles** — Persian is cursive/caseless (hard rule).
- **Scale:** 11 micro (paths, counts, step labels) / 12 captions+labels / 13 nav, quiet buttons /
  14 body, rows / 15 section+list titles / 17–18 identity+dialog titles / 22 screen titles /
  32 security score.

| Style key | Spec | Use |
|-----------|------|-----|
| `TitleText` | 22 SemiBold, Primary | screen + hero titles, empty-state titles |
| `DialogTitle` | 18 SemiBold | dialog titles |
| `SubtitleText` | 15 SemiBold, Primary | section titles, list header, details title base |
| `BodyText` | 14, Primary | values, copy |
| `SecondaryText` | 12, Secondary | hints, status, captions |
| `MicroText` | 11, Disabled | paths, counts, metadata |
| `SectionHeader` | 11 SemiBold, Secondary, margin `16,18,16,4` | sidebar group labels |
| `GroupHeader` | same, margin `0,0,0,4` | in-pane group labels (details, settings) |
| `FieldLabel` | 12 Secondary, margin `0,10,0,3` | label above each field |
| `LtrValueText` | Consolas + `FlowDirection=LTR`, left | **all technical values** (passwords, URLs, keys, codes) |

---

## 4. Iconography

- **Material Design Icons** via `materialDesign:PackIcon` (compile-checked `PackIconKind`).
- **Icon-avatar signature pattern:** rounded tile `Accent.Soft` + `Accent` glyph. Sizes:
  56 r16 (hero), 44 r12 (identity headers), 40 r10 (vault cards, backup option rows),
  36 r10 (dialog headers), 32 r9 (brand tile — solid `Accent` with white wordmark glyph),
  28 r8 (sidebar account), 72-circle r36 (empty states). Confirm dialogs use 40-circle
  `Danger.Soft` + `Danger`.
- **Type identity map** (`Services/ItemIconMapper.cs`) — same icon in nav, rows, details, editor:

| ItemType | PackIconKind | | ItemType | PackIconKind |
|----------|--------------|-|----------|--------------|
| Login | `Key` | | ApiCredential | `Api` |
| SecureNote | `NoteText` | | SoftwareLicense | `Certificate` |
| PaymentCard | `CreditCard` | | RecoveryCodes | `Dialpad` |
| Identity | `CardAccountDetails` | | Custom/fallback | `PackageVariantClosed` |
| Wifi | `Wifi` | | | |

- Severity: security findings get `AlertOutline` tinted by `FindingKindBrush` —
  Weak/Reused/Empty password → `Danger`; Old/MissingAuthenticator/Duplicate/Expiring → `Warning`.

---

## 5. Spacing, shape, elevation, motion

- **4-px grid.** Page margin 40; panel content padding 20 (workspace) / 24 (dialogs);
  card padding 14–16; group gap 14–20.
- **Radii:** controls 8 (`ButtonAssist.CornerRadius` on all button styles), rows/nav 8,
  avatars 8–16, chips 14, cards 12 (`CardBorder`), wells 10, **dialogs 16** (`DialogCard`).
- **Elevation:** flat. Workspace = flush panels separated by 1-px `Divider` **columns** (which
  mirror with FlowDirection — see §9). Cards/wells separate by surface color + hairline, never
  shadows.
- **Motion:** minimal; none on secret reveal; indeterminate `ProgressBar` (height 4) in
  overlays; determinate 8-px score bar in Security Center.
- **RTL margin law:** physical margins don't mirror. In stretch+auto column pairs the
  **stretching** element carries symmetric horizontal margins; auto siblings use symmetric
  `4,0`-style margins; **never** position with physical `Left`/`Right`; trailing-aligned
  footers use a `*`-width spacer column (grid columns mirror) instead of alignment.

---

## 6. Control style inventory (`Styles/Controls.xaml`)

Implicit: `Button` → `MaterialDesignRaisedButton` (padding `16,7`, radius 8, hand cursor);
Material MD3 filled TextBox/PasswordBox everywhere.

| Key | Base | Look & role |
|-----|------|-------------|
| `DangerButton` | raised | `Danger` bg+border — destructive primary only |
| `IconButton` | flat | quiet text button, `Text.Secondary`, 13px, padding `10,5` |
| `IconActionButton` | Material icon button | 32×32 square (28–28 variants inline) for copy/reveal/close; label in `ToolTip` + `AutomationProperties.Name` |
| `OutlineButton` | outlined | secondary emphasized (create vault) |
| `NavItemButton` | flat | full-width sidebar/overlay entries, 13px, radius 8, stretch |
| `NavRadioButtonItem` | RadioButton | sidebar + settings-nav entries; hover `Item.Hover`, **checked = `Accent.Soft` + `Accent` text**, disabled 0.45; padding `10,7` |
| `FilterChip` | RadioButton | pill chip r14; checked = `Accent.Soft` bg + `Accent` border/text — security filters, generator presets |
| `SearchBoxStyle` / `PasswordBoxStyle` | TextBox/PasswordBox | 14px, padding `10,7`, Material char counters **off** |
| `CardBorder` | Border | `Bg.Card` + 1-px `Divider`, r12 — vault cards, picker list |
| `DialogCard` | `CardBorder` | r16 — all modal surfaces |
| `WellBorder` | Border | `Bg.Sunken`, r10, no border — TOTP, outputs, recovery key |
| `SectionCard` | `CardBorder` | padding 16 — create-wizard password block |
| `StatCard` | `CardBorder` | padding `14,12`, r10 (reserved) |
| `MenuRowButton` | flat | backup option rows: severity-tinted icon + title + description, radius 10, **MinHeight 62** (works around an MD flat-template measure collapse — see `ui-overhaul-audit.md`) |

Buttons semantics: **one raised primary per footer**; `DangerButton` destructive only;
`IsDefault="True"` marks Enter. Dialog footers: Cancel quiet + primary raised, trailing via
spacer column.

---

## 7. Runtime converters (`App.xaml` keys)

| Key | Semantics |
|-----|-----------|
| `BoolToVisible` / `FalseToVisible` | true/false → Visible |
| `NullToVisible` / `NullToCollapsed` | **null → Visible** (empty states) / **non-null → Visible** (content) |
| `NullToBool` | non-null → true (IsEnabled gates) |
| `ZeroToVisible` / `ZeroToCollapsed` | count == 0 → Visible / count > 0 → Visible |
| `IndexEquals` | int == param (settings radio groups; writes back) |
| `EmptyToVisible` | empty string → Visible (search watermark) |
| `LocKey` | resolves a bound string as a localization key |
| `SectionEquals` | nav active state (`Section == "All"` …) |
| `TypeToIcon` / `TypeToLabel` | ItemType → PackIconKind / localized label |
| `FindingKindLabel` / `FindingDetail` | finding kind/detail → localized text |
| `FindingKindBrush` | finding kind → severity brush (Danger vs Warning), theme-live |

---

## 8. Localization & bidi

- Neutral culture = **fa**; `en` satellite; live switch; ~340 keys; **zero literal UI strings**
  in XAML/code (the two literals allowed: the Persian wordmark glyph fallback and
  `"فارسی"`/`"English"` language self-names in Settings).
- Mixed direction: `LtrValueText` (Consolas + LTR) for technical values; window `FlowDirection`
  flips the whole shell; the 1-px panel divider columns and spacer-column footers mirror
  automatically; grid-column-based step indicators stay direction-correct.
- Dates: Gregorian internal; Persian (Solar Hijri) display default, switchable.

---

## 9. Shell composition

```
MainWindow 1240×800 (min 1000×680), canvas bg, FlowDirection=ShellFlowDirection
├─ ContentControl {CurrentView}          ← page DataTemplates by VM type
│   ├─ VaultPickerViewModel              ← §10.1
│   ├─ CreateVaultViewModel              ← §10.2 (2 stages + step indicator)
│   ├─ UnlockViewModel                   ← §10.3
│   └─ VaultWorkspaceViewModel           ← §10.4 + workspace overlays (§10.5–10.7)
└─ App-level overlays (scrim): Settings §10.8 · Backup §10.9 · Import wizard §10.10
```

Workspace anatomy (rows 56 / \*):

```
┌──────────────────────────────────────────────────────────────────┐
│ TOP BAR 56 (canvas, hairline bottom): [گ tile][Ganjineh][·][vault]│
│                                       … theme ⇄  language  [Lock]│
├────────────┬────┬────────────────────────────┬────┬─────────────┤
│ SIDEBAR    │ 1px│ LIST (canvas)              │ 1px│ DETAILS     │
│ 236 surface│    │ header: title+count        │    │ 392 surface │
│ scroll: nav│    │         search / sort / New│    │ identity hdr│
│ Vault·Favs·│    │ rows (r8 hover; selected = │    │ field groups│
│ 8 types    │    │  Accent.Soft + leading bar)│    │ + dividers  │
│ Folders    │    │ Security Center spans      │    │ TOTP well   │
│ Tags       │    │  list+details when active  │    │ (hairlines  │
│ Tools      │    │ status bar 28 (sunken)     │    │  between)   │
│ pinned: ───│    └────────────────────────────┘    │ pinned acts │
│ account row│                                      └─────────────┘
│ Settings   │   (divider columns hide while Security spans)
└────────────┘
```

The two 1-px divider columns (and the details panel) collapse while `SecuritySectionActive`,
and the list border's `Grid.ColumnSpan` flips 1→3.

---

## 10. Screens

### 10.1 Vault picker
Centered column MaxWidth 640. Hero: ShieldKey avatar 56 r16 on `Accent.Soft`, `TitleText`
title, `Picker.Subtitle` caption. Vault cards: `CardBorder` padding 8 (`PickerList`) with
transparent `ListBox` (`VaultList`): row = DatabaseOutline avatar 40 r10, name 14 SemiBold,
last-opened 11 secondary, path 11 `Text.Disabled` ellipsized + full-path ToolTip
(`VaultCard`). Empty: `Picker.NoVaults` caption. Footer (centered): **raised default Unlock**
(`PickerUnlock`), `OutlineButton` Create (+16px Plus, `PickerCreate`), quiet Open existing
(`PickerOpenExisting`), quiet Remove (`PickerRemove`).

### 10.2 Create vault
MaxWidth 620. **Step indicator** (mirrored grid, 2 segments + labels): details active →
`Accent`; recovery active → segment 2 `Accent`, segment 1 `Accent.Soft` (done). Stage 1
(`CreateDetails`): `TitleText` title; name (100) + hint; location read-only + Browse; password
block in `SectionCard`: two `PasswordBox` (1000) + `StrengthHint` caption. Stage 2
(`RecoveryStage`): explain; key in `WellBorder` padding 16, 16 Medium `LtrValueText` centered
(`RecoveryKeyText`); copy (`RecoveryCopy`); challenge prompt + input (MaxLength 8) + error.
Footer: inline error, working text, quiet Back, raised default Next/Confirm (`CreateNext`,
`RecoveryConfirm`).

### 10.3 Unlock
MaxWidth 460. Identity row: LockOpenOutline avatar 44 r12 + vault name 17 SemiBold
(`UnlockVaultName`) + path micro (+tooltip). Password pane (`PasswordBox`, Enter submits) /
recovery pane toggled by `RecoveryMode`. Inline `Danger` error + working text. Footer:
spacer-column trailing — quiet Cancel + quiet Use-recovery, raised default Unlock
(`UnlockButton` / `UnlockConfirmRecovery`).

### 10.4 Workspace
**Top bar:** brand tile 32 r9 solid `Accent` + white `{loc:Loc App.Brand}` glyph, wordmark,
dot separator, vault name 14 SemiBold (`WorkspaceVaultName`); trailing: `IconActionButton`
theme (`WeatherNight`⇄`WeatherSunny`, `ToggleThemeButton`), language (`Translate`,
`ToggleLanguageButton`), **raised Lock** (`LockVaultButton`) — the only emphasized action.

**Sidebar** (236 surface): ScrollViewer (`NavScroller`) — sections via `SectionHeader`;
`NavRadioButtonItem`s (18px icons): All/Favorites/8 types (`NavAllItems`, `NavFavorites`,
`SetSectionCommand` params `All|Favorites|Type:X|Security|Trash`, `SectionEquals` active
state); Folders: input (`NewFolderInput`, 100) + add/delete 28px `IconActionButton`s
(`AddFolderButton`, `DeleteFolderButton`) + `FolderList` (16px `FolderOutline` + count);
Tags likewise (`NewTagInput`, `AddTagButton`, `DeleteTagButton`, `TagList`); Tools:
Generator (`OpenGenerator`), Security Center radio (`OpenSecurityCenter`), Backup
(`OpenBackup`), Trash (`NavTrash`). **Pinned footer** (below a hairline): account row —
ShieldKey avatar 28 + name 13 (`SidebarVaultName`) + switch `IconActionButton`
(`SwitchVaultButton`) — and Settings (`NavSettings`). Always on screen; never scrolls away.

**List region** (canvas): header (hairline bottom, padding `20,12`) — title row: `ListHeader`
15 SemiBold + live count micro (`ListCount`: `RowsView.Count` + `List.CountSuffix`); controls
row: white bordered search (r8, `Divider.Strong` border, Magnify 18, debounced 150 ms
`SearchBox`, watermark `Search.Placeholder`), sort combo 140 (`updated|title|created`),
raised **New item** (`AddCredential`, padding `12,6`). Rows (`CredentialList`): r8 row
(`CredentialRow`) — transparent → `Item.Hover` on hover → `Accent.Soft` + 3-px **leading**
`Accent` bar (grid column 0) when selected; avatar 36 r10 (`TypeToIcon`), title 14 Medium
(`RowTitle`), subtitle 12 (`RowUsername`), `Star` 14 `Warning` when favorite. Empty
(`CenterEmpty`): 72 soft circle + ShieldOff 32 + 15 SemiBold + caption.

**Security Center** (spans; `SecurityCenter`): score hero — 32 SemiBold `Accent`
(`SecurityScore`) + caption + quiet refresh (`SecurityRefresh`) + working text; offline note;
8-px determinate bar (0–100); `FilterChip` row (all + 7 kinds with counts, `FilterFindingsCommand`);
findings list (`FindingsList`) — flat rows with severity-tinted `AlertOutline`, kind 13 Medium
+ title 13 + detail 11; "no findings" caption at zero.

**Details** (392 surface; `DetailsPane`): identity header padding `20,16,20,8` — avatar 44 r12,
title 18 (`DetailTitle`), username caption (`DetailUsername`), favorite toggle
(`ToggleFavorite`, `StarOutline`⇄`Star` `Warning`). Scrollable groups separated by hairline
`Rectangle`s (no cards): Login fields (username+copy `CopyUsername`; password + reveal
`RevealPassword` `Eye`⇄`EyeOff` + copy `CopyPassword`; website + open); **TOTP well**
(`WellBorder`, `TotpSection`): copy 28px (`CopyTotp`), code 22 SemiBold `Accent` `LtrValueText`
(`TotpCode`) + seconds (`TotpSeconds`) + factor warning; type fields (masked rows → Consolas
LTR + reveal/copy); custom fields (`DetailCustomRows` + copy); notes; attachments (all types;
export/delete 28px rows + `AddAttachment`); history (auto-loaded, `RestoreVersion` enabled
only with selection, `VersionList` max 130); created/modified line (`DetailDates`).
**Pinned footer** (hairline top): trailing spacer grid — normal: `DangerButton` Delete
(`DeleteCredential`) + raised Edit (`EditCredential`); trash: quiet Empty (`EmptyTrash`) +
raised Restore (`RestoreFromTrash`) + `DangerButton` Purge (`PurgeFromTrash`).

**Status bar** (list column bottom, 28, sunken, hairline top): `StatusMessage`
(`StatusText`, margin 20).

### 10.5 Editor overlay (workspace scrim; `EditorPane`)
`DialogCard` MaxWidth 780 / Min 600. Header: type avatar 36 r10 + `DialogTitle`
(new/edit via `EditingId` trigger) + close. Body scroll MaxHeight 620: type picker (create
only); login fields (Title 200 / Username 320 / `PasswordBox` 1000 + keep-hint **editing
only** / Website 2048 / Notes multiline 10 000); non-login dynamic rows (single-line 2000 /
multiline / `CheckBox`); description; custom fields grid **2\* / 130 / 2\* / Auto / Auto**
(label 100, type combo MinWidth 130, value 5000, conceal check, remove ✕) + full-width add;
**folder + tags side-by-side** (uniform 2 columns, gutter 20). Footer (hairline top,
trailing): quiet Cancel (`EditorCancel`) + raised default Save (`EditorSave`).

### 10.6 Generator overlay (`GeneratorPane`)
`DialogCard` MaxWidth 680, body scroll 640. Header: `AutoFix` avatar 36 + `DialogTitle`.
Mode radios (`GenMode`): Password/Passphrase. Character mode: output `WellBorder` 16 SemiBold
`LtrValueText` (`GeneratorOutput`) + entropy caption; preset chips (`ApplyPresetCommand`:
Standard/Strong/MaximumCompatibility/NumericPin); length slider 8–64 (`GeneratorLength`);
charset checks (a-z, A-Z, 0-9, !@#, no-ambiguous); excluded chars (`GeneratorExcluded`, 100).
Passphrase mode: output well (`PassphraseOutput`), words 3–12 (`PassphraseWords`),
separator (MaxLength 3, width 80, `PassphraseSeparator`), capitalize/number checks. Strength
verdict caption in `Warning` (known limitation — string-only from the VM). Footer: quiet
Regenerate (`Regenerate`), quiet Copy (`CopyGenerated`), raised Use-in-editor
(`UseInEditor`, editor-targeted only), quiet Close (`CloseGenerator`).

### 10.7 Confirm overlays (workspace scrims; 440 `DialogCard`)
Header: 40-circle `Danger.Soft` + `Danger` icon + `DialogTitle` (`AlertOutline` delete /
`DeleteOutline` empty trash); body text (delete names the item, SemiBold run); trailing
footer: quiet Cancel + `DangerButton` confirm. IDs: `DeleteConfirmPane`/`DeleteCancel`/
`DeleteConfirm`; `EmptyTrashConfirmPane`/`EmptyTrashCancel`/`EmptyTrashConfirm`.

### 10.8 Settings overlay (app-level; `SettingsPane`)
**680 `DialogCard` with internal category nav** — left rail of `NavRadioButtonItem`s
(General `Tune` / Appearance `WhiteBalanceSunny` / Security `ShieldCheck` / Import
`DatabaseImportOutline`), content panels switched by XAML-local `ElementName` radio bindings
(**no VM state**); every control persists immediately:
- General: language radios (`فارسی`/`English` ↔ `Settings.LanguageIndex`).
- Appearance: theme radios ↔ `Settings.ThemeIndex`.
- Security: clipboard slider 220px 10–120 step 5 + seconds + hint; auto-lock combo
  `[0,1,5,10,15,30]` (0 = "never"), Width 200.
- Import: Chrome / Firefox `NavItemButton`s (`ImportFromChrome`, `ImportFromFirefox`) +
  no-passwords-in-preview note.
Close: `SettingsCloseButton`.

### 10.9 Backup & restore overlay (app-level; `BackupPane`)
560 `DialogCard`. Header `BackupRestore` avatar 36 + `DialogTitle` + close; indeterminate
bar while busy. **Menu stage**: three `MenuRowButton`s — Create (accent icon,
`BackupCreate`), Verify (success icon, `BackupVerify`), **Restore (danger icon — visibly
sensitive**, `BackupRestore`), each 22px tinted PackIcon + title 14 SemiBold + localized
description caption (12, wraps; rows have a 62px height floor — MD flat-button measure
workaround). **Restore stage**: path (Consolas caption), destination read-only + Browse,
`PasswordBox` (1000), trailing footer Cancel + raised confirm (`BackupRestoreConfirm`).
`VerifySummary` + `BackupStatus` (accent) captions.

### 10.10 Import wizard (app-level; `ImportWizardPane`)
**860 `DialogCard`** (min 640), shared Chrome/Firefox (header icon switches on
`BrowserIndex`). **Visual step indicator** (`ImportStepLabel` — 4 mirrored grid segments +
labels Export/File/Preview/Done): active `Accent`, done `Accent.Soft`, future `Divider`,
driven by `IsInstructionsStep`/`IsFileStep`/`IsPreviewStep`/`IsProgressStep`.
1. Instructions: per-browser how-to; caution box (1-px `Warning`, r8, `AlertOutline` +
   plain-text warning); Cancel + raised Proceed (`ImportInstructionsNext`).
2. File: read-only path (`ImportFilePath`) + Browse (`ImportBrowse`); `Danger` error
   (`ImportFileError`); Cancel + outlined Parse (`ImportParse`); indeterminate bar.
3. Preview: counts (`ImportCounts`); filter with Material hint (`ImportFilter`) +
   select/deselect all; `GridView` columns ☑36 / Title 150 / Origin 170 / Username 140 /
   Status 130 / Duplicate-of 110 / Action 130 (skip/update/copy combo, duplicates only);
   **no passwords in preview**; Cancel + outlined back + raised `ImportBegin`.
4. Progress: indeterminate bar; processed/total (`ImportProgress`); summary (`ImportStatus`);
   failures in `Danger`; post-completion cleanup warning box (`FileAlertOutline`); Cancel +
   outlined open-source-folder (`ImportOpenFolder`) + raised show imported
   (`ImportShowItem`).

---

## 11. Global states and security behaviors

Every screen defines: **empty** (`ZeroToVisible` placeholders), **busy** (caption or
indeterminate bar), **error** (inline `Danger` caption), **locked** (return to picker,
bindings cleared).

Secret rules: hidden by default; explicit `Eye`⇄`EyeOff` reveal, auto re-hide (30 s / focus
loss); no reveal animation; copies go through the TTL clipboard service (default 60 s,
setting 10–120) and never auto-fire; import previews never show passwords; destructive
actions always require the confirm overlay; restore requires the master password.

## 12. Keyboard & accessibility

`Esc` closes the top overlay; `Enter` acts on the focused form's `IsDefault` button.
Global: `Ctrl+F` search, `Ctrl+N` new (no overlay), `Ctrl+G` generator (no overlay),
`Ctrl+L` lock. Icon-only buttons always carry localized `AutomationProperties.Name` +
`ToolTip`; contrast AA both themes; nav is RadioButtons (persistent active state); focus
stays in overlays.

## 13. Automation ID register

All IDs from the previous export are preserved; additive changes: `ListCount`,
`SidebarVaultName`, `VaultCard`. Full register: see `git history` of this file or
`MainWindow.xaml` (`AutomationProperties.AutomationId`). **Do not rename or remove IDs** —
the FlaUI suites (`Phase3UiTests`, `Phase9UiTests`, import journey tests) and
`VisualReviewTests` (screenshots → `artifacts/ui-review/`) target them. Harness-critical
minimum: `PickerCreate` `CreateName` `CreatePassword` `CreatePasswordConfirm` `CreateNext`
`RecoveryKeyText` `RecoveryChallengePrompt` `RecoveryChallengeInput` `RecoveryConfirm`
`AddCredential` `EditorTitle` `EditorUsername` `EditorPassword` `EditorWebsite` `EditorSave`
`CredentialList` `DetailPassword` `OpenGenerator` `GeneratorOutput` `CloseGenerator`
`OpenSecurityCenter` `NavAllItems` `NavSettings` `SettingsCloseButton` `ToggleThemeButton`
`ToggleLanguageButton` `OpenBackup` `BackupCreate` `BackupCloseButton` `DeleteCredential`
`DeleteConfirm` `DeleteCancel` `NavScroller`.

## 14. Rules for agents touching this UI

1. **Strings:** resx keys (fa **and** en, parity-tested), referenced with `{loc:Loc Key}`.
2. **RTL margins:** §5 law — symmetric margins on stretching elements, spacer columns for
   trailing alignment, never physical Left/Right alignment for flow positioning; verify fa
   **and** en.
3. **Colors:** only `DynamicResource` token keys; new keys go into **both** theme dictionaries.
4. **Buttons:** one raised primary per footer; `DangerButton` destructive only; icon-only
   actions need `ToolTip` + `AutomationProperties.Name`; keep `AutomationId`s stable.
5. **Secrets:** hidden by default, `LtrValueText` values, TTL clipboard only, no secrets in
   previews or screenshots (visual harness uses synthetic data).
6. **Verify:** `dotnet build` (zero warnings — repo treats warnings as errors) → unit suites →
   FlaUI E2E → `VisualReviewTests` screenshots inspected in fa/light, fa/dark, en/light
   before declaring a UI change done.
