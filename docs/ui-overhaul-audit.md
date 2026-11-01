# UI Overhaul — Critical Visual Audit & Art Direction (2026-11-01)

Stage A of the premium redesign brief. Audits the as-built UI (see `ui-design-export.md`) screen
by screen, decides what is kept / improved / replaced, and fixes the new art direction.
Stages B–G implement it; this file is the design rationale of record.

## 1. Verdict on the current UI

The current UI is *consistent and safe* but reads as a **generic Material sample**, not a
product: default Material Blue on neutral grey, three same-weight floating cards in the
workspace, a card-with-hairline border around every content group, dialog widths chosen
per-screen (440–720 px) with no scale, and a 480 px Settings modal stacking five unrelated
sections. Nothing is broken; nothing is memorable.

### What is retained (it works)

- Single-window shell, page = `DataTemplate` per VM, scrim-overlay pattern, `Esc`/`IsDefault`
  behavior (app-level interaction model is right).
- RTL-first authoring + the symmetric-margin law (physical margins don't mirror) — this is a
  strength; the redesign keeps it and extends it (flow-safe trailing alignment via mirrored
  Grid spacer columns, never physical Left/Right for flow positioning).
- Automation-ID discipline; the FlaUI/visual harness contract (34 IDs drive the E2E and
  screenshot passes) — all IDs preserved through the redesign.
- Security behaviors: hidden-by-default secrets, TTL clipboard, confirm overlays, typed
  restore; the offline-security-center honesty (no fake breach checks).
- Vazirmatn; LTR isolation for technical values; no letter-spacing/uppercase on Persian.

### Defects to fix (area → finding → decision)

1. **Palette** — Material Blue `#1976D2` + neutral greys = default-looking; hover/selected
   states are barely-there alpha blacks; dark mode is grey, not designed. → **Replace** with an
   Indigo/Slate system (§3); dark mode designed on Slate-900/800, never inverted.
2. **Workspace composition** — nav/list/details are three `CardBorder` boxes floating on grey
   with 10 px gutters: a widget grid, not an app. → **Replace** with an app shell: white
   sidebar panel + canvas list region + white details panel, separated by hairlines, no
   floating cards. Status bar spans only the list column (details keeps its pinned footer).
3. **Over-carding** — details pane = 7 hairline-bordered `SectionCard`s; workspace = 3 cards;
   every dialog another card. Borders are doing spacing's job. → **Reduce**: sections in
   details become labeled groups separated by hairline dividers; cards remain only where a
   surface genuinely floats (dialogs, vault cards, output wells).
4. **Sidebar** — 240 px, everything permanently expanded; inline add-folder/add-tag input rows
   with two icon buttons each are permanent clutter; no pinned bottom area; nav items tall.
   → **Improve**: compact items (13 px, tighter padding), section headers restyled, folders/tags
   rows tightened (IDs kept: the inline input + add/delete buttons stay, quieter), Settings +
   vault account block pinned at the bottom of a non-scrolling footer, scrolling middle.
5. **List region** — rows on a card with no hover/selected identity beyond defaults; no item
   count; search box is a bespoke sunken border; sort combo unlabeled and cramped; New button
   large. → **Improve**: continuous list on canvas, rounded-8 hover wash, selected row =
   accent-soft fill + 3 px leading accent bar; header shows contextual title + live count;
   white bordered search field; compact sort; raised New stays (primary of the region).
6. **Details pane** — fixed 380 px on a 980 px-min window leaves the list 320 px; header is
   fine; TOTP is plain text; actions pinned (good). → **Improve**: 392 px panel (still fits
   min-window arithmetic), identity header enlarged (44 avatar, 18 SemiBold title), field
   groups as label + hairline sections, TOTP as a sunken well with 22 px code + copy inline,
   footer unchanged in function.
7. **Top bar** — brand tile tiny, four equal-weight controls, Lock reads like a sibling of
   theme/language. → **Improve**: brand tile + wordmark, vault identity next to it, quiet
   icon toggles, Lock as the only emphasized (raised) action. Theme/language remain in Settings
   as well.
8. **Dialog scale** — widths 440/440/480/520/540/620/720 chosen ad hoc; footers centered;
   close button floats. → **Standardize**: width scale S=440 (confirms) / M=560 (unlock,
   backup) / L=680 (create, generator, settings) / XL=860 (editor, import wizard); header =
   18 SemiBold title (+optional caption), trailing close; footer = flow-safe trailing
   alignment (Grid spacer trick), Cancel quiet + one primary.
9. **Settings** — five unrelated sections in a 480 px column. → **Replace** with a 680 px
   dialog with internal category nav (General / Appearance / Security / Data import), panels
   switched by XAML-local radio state (no VM change); immediate persistence kept.
10. **Import wizard** — 720 px for a 7-column preview; text-only step indicator. → **Improve**:
    860 px dialog, visual 4-segment step indicator (grid-mirrored, RTL-safe), rebalanced
    column widths, duplicate action combo wider; plaintext-password exclusion unchanged.
11. **Security Center** — score is a number + 6 px bar; all findings share one warning icon;
    chips fine. → **Improve**: score hero (32 px) + severity-colored finding icons
    (new kind→brush converter: weak/reused/empty = danger, others = warning), restyled
    findings rows; offline note retained verbatim.
12. **Generator** — everything at equal priority; strength verdict rendered in Warning color
    even when strong (VM returns a string; no score property — left as a listed defect, needs
    a VM-level presentation property, out of the "no business-logic changes" line this pass).
    → **Improve**: output as a hero well with inline copy button, presets as chips, settings
    grouped; entropy caption kept.
13. **Vault lifecycle** — picker footer has four equal buttons; paths are prominent; unlock is
    bare; create has no step affordance. → **Improve**: hero brand mark, vault cards with
    clear hierarchy (Unlock raised / Create outlined / rest quiet), path demoted to caption +
    tooltip; unlock gets a vault identity block; create gets a 2-step segmented indicator.
14. **Empty states** — oversized glyphs, weak composition. → Standardize: 40 px icon in a
    soft-accent circle, 15 SemiBold title, 12 caption.
15. **Window metrics** — 1180×780 default is small for a three-pane desktop tool. → Default
    **1240×800**, min **1000×680** (236 sidebar + 320 list + 392 details + gutters fit 1000).

### Explicit non-goals (this pass)

- No split of `MainWindow.xaml` into per-view files: seven templates use code-behind `Click`
  handlers (`OnUnlockPasswordKeyDown`, `OnCreateNextClick`, `OnCopyRecoveryKeyClick`,
  `OnEditorSaveClick`, `OnAddAttachmentClick`, `OnExportAttachmentClick`,
  `OnBackupRestoreConfirmClick`), which cannot resolve from merged ResourceDictionaries
  without converting the PasswordBox handoff to commands — a functional-risk change out of
  proportion for a visual pass. The file is reorganized with region banners instead; the split
  is documented as future work tied to a command-based handoff refactor.
- No strength meter (needs a new VM-computed score property), no new security metrics, no
  animations on secrets.

### Platform gotchas discovered during implementation (verified)

- **MaterialDesignFlatButton rich-content mis-measure:** a flat button whose content is an
  icon + stacked title/description collapses to ~32 px at measure time and center-clips its
  full-size content (UIA bounding rects confirmed full-size text in a one-line button).
  Fixed deterministically with `MinHeight="62"` + `VerticalContentAlignment="Center"` on
  `MenuRowButton`; root cause is inside the MD 5.3.2 flat template, not the content tree.
- **Collapsed panels vanish from the UIA tree:** the settings category rail's Import panel is
  `Collapsed` until its radio is selected, so FlaUI cannot find `ImportFromChrome` inside it —
  `Phase9UiTests` now selects the category first (same step a user takes).
- **WPF `HorizontalAlignment.Left/Right` is flow-relative** (mirrors with FlowDirection);
  physical `Margin`s do not mirror. Flow-safe trailing alignment uses a `*`-width spacer
  column; the confirm/editor/settings footers all use it.

## 2. New art direction — "Calm Slate & Indigo"

- **Semantic palette:** cool slate structure (not warm grey), indigo as the single accent;
  amber/red reserved for security semantics (warning/danger), green for success. Premium =
  restraint: one hue + neutrals.
- **Material `BundledTheme` moves to Indigo** (Primary+Secondary) so Material chrome
  (sliders, combos, checkboxes, focus) follows the brand; ThemeService and App.xaml updated
  together.
- **Surfaces:** canvas (app background) vs surface (sidebar/details/dialogs) vs sunken (wells,
  status) — three levels; hierarchy from background + hairline, shadows only on dialogs'
  scrim contrast (no shadow geometry in RTL).
- **Shape:** controls 8 / cards 10–12 / dialogs 16.
- **Type:** Vazirmatn; 24 SemiBold screen titles, 18 dialog titles, 15 section titles,
  14 body, 13 nav/quiet buttons, 12 captions/labels, 11 micro (paths, counts); Medium/SemiBold
  for hierarchy instead of size jumps.
- **Icon-avatar system** retained (soft-accent tile + accent glyph) as the app's signature;
  sizes harmonized (28/32/36/40/44/56 tiles).
- **Motion:** unchanged (minimal; no secret-reveal animation).

## 3. Final token tables (implemented in `Themes/Light|Dark.xaml`)

Existing key names are kept (consumers keep resolving); values change, and new keys are added
in **both** dictionaries (key parity is a hard rule).

| Token | Light | Dark | Role |
|-------|-------|------|------|
| `Bg.Base` | `#F8FAFC` | `#0F172A` | canvas |
| `Bg.Card` | `#FFFFFF` | `#1E293B` | surface: sidebar, details, dialogs, wells |
| `Bg.Sunken` | `#F1F5F9` | `#131C2F` | sunken wells, status bar, search fill |
| `Text.Primary` | `#1E293B` | `#F1F5F9` | primary text |
| `Text.Secondary` | `#64748B` | `#94A3B8` | secondary text, quiet icons |
| `Text.Disabled` *(new)* | `#94A3B8` | `#64748B` | disabled/tertiary (paths, counts) |
| `Accent` | `#4F46E5` | `#818CF8` | indigo accent |
| `Accent.Hover` | `#4338CA` | `#6366F1` | accent hover |
| `Accent.Soft` | `#EEF2FF` | `#2A2F55` | avatar fills, selected nav/rows |
| `Danger` | `#DC2626` | `#F87171` | destructive, errors |
| `Danger.Soft` *(new)* | `#FEF2F2` | `#2EF87171` | destructive hover tint |
| `Success` | `#16A34A` | `#4ADE80` | success |
| `Success.Soft` *(new)* | `#F0FDF4` | `#2E4ADE80` | success tint |
| `Warning` | `#D97706` | `#FBBF24` | favorites, cautions |
| `Warning.Soft` *(new)* | `#FFFBEB` | `#2EFBBF24` | caution tint |
| `Divider` | `#E2E8F0` | `#334155` | hairlines |
| `Divider.Strong` *(new)* | `#CBD5E1` | `#475569` | emphasized borders (inputs) |
| `Item.Hover` | `#0F1E293B` | `#14FFFFFF` | row/nav hover wash |
| `Overlay.Scrim` | `#4D0B1220` | `#990B1220` | modal scrims (slate-tinted) |

Contrast: text on surface ≥ 4.5:1 in both themes (Slate-800/White = 12.6:1; Slate-100/Slate-900
= 14.2:1; secondary pairs ≥ 4.6:1). Accent used for interactive elements only.

## 4. Layout architecture (implemented in Stage C)

```
MainWindow 1240×800 (min 1000×680), canvas bg
├─ Top bar 56 (canvas, hairline bottom): brand tile+wordmark · vault name · spacer ·
│   theme ⇄ · language · [Lock raised]
├─ Body grid: [236 sidebar | * list | 392 details]
│   ├─ Sidebar (surface, hairline leading edge): ScrollViewer middle (nav) +
│   │   pinned footer: separator, vault account row (avatar+name+switch), Settings gear
│   ├─ List region (canvas): header (title+count / search / sort / New) →
│   │   continuous rows (r8 hover, selected = accent-soft + leading bar) or
│   │   Security Center (ColumnSpan 2 over list+details as before) →
│   │   status bar 28 (sunken, list column only)
│   └─ Details (surface, hairline trailing edge): identity header → grouped fields
│       (labels + hairline dividers, TOTP well) → pinned action footer
└─ Overlays (scrim): confirm S=440 · backup M=560 · create/generator/settings L=680 ·
    editor/import XL=860; header/title/close + trailing-aligned footer (Grid spacer)
```

## 5. Verification contract (Stages C–G)

After every stage: `dotnet build` (zero warnings, warnings-as-errors) → affected unit tests →
FlaUI E2E (`Phase3UiTests`, `Phase9UiTests`, import journey tests) → at Stage G the visual
harness (`VisualReviewTests` → `artifacts/ui-review/*.png`) inspected image by image in
fa/light, fa/dark, en/light; defects fixed and re-captured. All 34 harness-driven automation
IDs must keep their names and reachability; `NavScroller` may become a no-op (compact sidebar)
— the helper tolerates that.

## 6. Accessibility review (post-implementation)

- **Contrast:** text/surface pairs ≥ 4.5:1 in both themes (Slate-800 on white 12.6:1,
  Slate-100 on Slate-900 14.2:1, secondary pairs ≥ 4.6:1, Disabled-tone text restricted to
  non-essential metadata). Accent is never used for body text on tinted fills.
- **Screen readers:** every icon-only button carries localized `ToolTip` **and**
  `AutomationProperties.Name`; the settings category rail and sidebar nav are RadioButtons
  (selection state exposed); live regions: status bar message, list item count (`ListCount`),
  security score. All harness-critical elements expose stable `AutomationId`s.
- **Keyboard:** full path preserved — `Esc` overlay stack, `Enter` on `IsDefault`,
  `Ctrl+F/N/G/L`; nav/list selection keyboard-driven; focus stays inside modal overlays; the
  pinned sidebar footer keeps Settings reachable without scrolling.
- **RTL/LTR:** no physical alignment for flow positioning (grid spacer columns and mirrored
  divider columns instead); step indicators are uniform mirrored grids; technical values stay
  LTR-isolated (Consolas) in both directions; directional icon glyphs mirror with
  `FlowDirection`.
- **Known gaps (deferred):** focus-ring visibility relies on Material defaults (not audited
  pixel-level); no high-contrast theme variant; list rows expose selection visually but not
  via a semantic SelectionItem pattern beyond the underlying `ListBox`.
