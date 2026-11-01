# Phase 4 Test Results

Date: 2026-10-22 | Suite: `dotnet test Ganjineh.slnx` — **137/137 green**, zero warnings.

## New coverage (Phase 4)

| Area | Tests | Highlights |
|------|-------|-----------|
| ChunkedAead protocol | 5 | multi-chunk round-trip, per-index nonces, reorder/truncation/total-size/attachment-binding/tamper rejection (raw crypto layer) |
| Item kinds (9 × theory) | 9 | every kind round-trips with Persian/Unicode data + custom fields; at-rest sentinel scans per kind |
| Custom fields | 1 | per-type validation (label required, URL/number checks), ordering + concealed preserved |
| Folders | 1 | nesting, cycle rejection (move parent under child), re-parenting on delete, items→root fallback |
| Tags | 1 | normalized dedupe (trim/space), usage counts, FK-cascade removal |
| History | 1 | version append, retention cap (50→3 in test), restore-appends-new-version, historical secrets encrypted at rest |
| Trash | 1 | soft delete, exclusion from lists, restore, purge |
| Attachments | 3 | multi-chunk streaming round-trip (5 MiB + tail) with plaintext-free blob, tamper → export fails + partial removed, delete removes blob; oversize rejection (100 MB) + orphan cleanup; cross-vault export rejection |
| Search | 1 | covers title/folder/tag across kinds, trashed excluded, live vs trashed indexes |
| Migration | 1 | Phase 3 vault reopens under v2 schema; legacy login payload readable through BOTH the legacy service and the unified service (v0-compat deserializer); schema version = 2 |

## Full-suite totals

137 tests across 7 assemblies: Domain 5, Persistence 4, Application 2, Cryptography 65
(incl. 5 new chunked-AEAD protocol tests), Integration 53 (24 Phase 3 + 19 Phase 4 + 10 legacy),
Security 7, Desktop.UITests 1 (full user-journey E2E, still green after the Phase 4 rework).

## Issues found and fixed during the phase

1. Attachment blob was never renamed from `.part` to its final path (metadata could reference a
   missing file) — fixed with the durable rename step + regression test.
2. Search selection was wiped by the debounce-triggered rebuild — selection now survives
   filtering when the item remains visible.
3. Folder-delete test assertion was wrong (children reparent to the *grandparent*, not
   themselves) — corrected.

## Known limitations (documented, deferred)

- Folder nesting UI is one level deep (schema supports more).
- Custom item templates (reusable field sets) are not in the UI yet — custom items with manual
  fields are.
- History "compare versions" shows side-by-side field lists only (no diff highlighting).
- Trash retention auto-purge runs through `EmptyTrashAsync(retentionDays)`; the background
  sweep at unlock is a Phase 5 wiring item.
- Attachment UIA test coverage lands with the Phase 5 UI test expansion.
