# Phase 4 Implementation — Item Types, Organization, History, Trash, Attachments

Status: Implemented and tested (phase-4-test-results.md) | 2026-10-22

## What was built

1. **Unified item architecture** — one `items` row + append-only `item_versions` envelope per item
   covers all nine kinds (Login, Secure Note, Payment Card, Identity, Wi-Fi, API Credential,
   Software License, Recovery Codes, Custom). Payload **v1** = `{v, title, description, fields[],
   data{}}` where `fields` are custom fields and `data` is the type-specific object.
   **Backward compatibility:** Phase 3 login payloads (`{"T","U","P","W","N"}`) are detected at
   read time and upgraded in memory to v1 Login payloads; on-disk upgrade is lazy (next save).
   No ciphertext was rewritten — migration is purely additive (schema v2).
2. **Custom fields engine** — 8 field types (Text, Concealed, Multiline, Url, Email, Number,
   Date, Boolean) with stable GUIDs, labels, order, concealed flag and per-type validation;
   supported across every item kind.
3. **Folders** — one nesting level via `parent_id` (v1 simplification kept; the schema allows
   deeper trees), encrypted names AAD-bound to their folder ID, cycle-safe moves (ancestor walk),
   delete with items→root fallback or items→trash (explicit choice), transactional.
4. **Tags** — encrypted names, duplicate prevention by normalized comparison (trim + invariant
   lowercase + Persian folding + spaces→dash), usage counts, FK-cascaded assignment.
5. **Favorites** — per-vault flag, nav section, filter, toggle command.
6. **History** — every save appends a version (cap 50 per item, oldest pruned); browse list;
   restore appends the old state as a NEW version (current retained); historical secrets stay
   encrypted and out of the search index.
7. **Trash** — soft delete (`trashed_at`), restore, purge, empty-trash with retention days
   (0–3650, default immediate for explicit empty; auto-purge hook available at unlock);
   trashed items excluded from lists and the search index.
8. **Attachments** — 4 MiB chunked AEAD per cryptographic-specification.md §8 (see
   attachment-encryption.md), streamed, 100 MB cap, failure-safe ordering (blob before metadata),
   orphan cleanup, cross-vault rejection, tamper/truncation detection.
9. **Advanced search** — kind/folder/tag/favorite/date filters + sorting over the in-memory
   password-free index; folder and tag names are searchable; trashed items excluded.
10. **Persian WPF** — nav with all kinds + favorites + folders/tags panels (create/delete +
    counts) + trash; type-glyph rows; details pane with masked/reveal/copy rows, favorite,
    history, attachments and trash actions; metadata-driven editor (`ItemFormRegistry`) with
    custom-field management; unimplemented tools remain explicitly disabled.

## Database migration (v1 → v2)

`SchemaMigrator.MigrateToCurrent` runs on every vault open/create: creates `schema_migrations`,
applies v2 inside one transaction (`folders`, `tags`, `item_tags`, `attachments`,
`items.folder_id`, indexes) and records versions. Fresh databases are created directly at v2.
A failed migration rolls back to the pre-migration state; Phase 3 rows are never rewritten.
