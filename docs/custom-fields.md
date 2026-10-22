# Custom Fields, Folders & Tags — as built (Phase 4)

## Custom fields (`custom-fields.md` contract)

- `CustomField { Id (GUID string), Label, Type, Value, Order, Concealed }` — stored inside the
  item payload envelope (never separate plaintext rows).
- Types: Text, Concealed, Multiline, Url, Email, Number, Date, Boolean. Values are strings;
  dates are ISO `yyyy-MM-dd`, booleans `"true"/"false"`, numbers invariant-parsed.
- Validation (`FieldValidation`): label required (≤100); per-type value checks run only when a
  value is present (fields are optional) — Url must be absolute http(s), Email must contain '@',
  Number invariant-parses, Date invariant-parses, Boolean is exactly true/false.
- Ordering is explicit (`Order`, editor Move-Up) and preserved on read.
- Concealed fields render masked in the details pane, are excluded from the search index, and
  support copy (reveal-on-demand per the details pane rules).
- The editor supports add / remove / move-up / label+type+value+concealed editing.

## Folders

- One nesting level in the v1 UI (`parent_id` column supports deeper trees later).
- Names encrypted with AAD binding to the folder ID — swapping name envelopes between folders
  fails authentication.
- Create / rename / delete from the nav panel; delete re-parents children to the deleted
  folder's parent and moves contained items to the root (non-destructive default) — items can
  alternatively be trashed by the explicit `deleteItems` path (service-level).
- Cycle prevention: moves validate the ancestor chain (`IsDescendant` walk, hard-bounded) —
  moving a folder into itself or a descendant is rejected (`FolderCircularMove`).
- Item counts per folder shown in the nav (`FolderItemCounts`).

## Tags

- Names encrypted, per-vault by construction (each vault DB holds its own tags).
- Duplicate policy: names equal after trim + invariant lowercase + Persian normalization
  (ي/ى/ك folding, diacritics, ZWNJ) + inner spaces→dash are the same tag; creating a duplicate
  returns the existing tag; renaming onto an existing name is rejected (`TagDuplicate`).
- Assignment: many tags per item via `item_tags` (FK cascade on item or tag delete).
- Usage counts shown in the nav; filtering by tag via the nav selection.

Both names and tag assignments are transactional where multi-row (folder delete, tag sets).
