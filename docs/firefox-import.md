# Firefox CSV Import

Supported source: the CSV produced by Firefox's built-in password manager export
(Settings → Privacy & Security → Logins and Passwords → "…" → Export Logins…). Firefox warns
that the file is unencrypted before writing it.

## Recognized schema (header-based mapping)

| Header | Required | Mapped to |
|--------|----------|-----------|
| `url` | **yes** | Login URL list (first entry) |
| `username` | **yes** | `username` data key |
| `password` | **yes** | `password` data key |
| `httpRealm` | no | — (reported as not imported) |
| `formActionOrigin` | no | — (reported as not imported) |
| `guid` | no | `sourceRecordId` provenance key |
| `timeCreated` | no | — (reported as not imported) |
| `timeLastUsed` | no | — (reported as not imported) |
| `timePasswordChanged` | no | `sourceTimePasswordChanged` provenance key (PRTime µs → Unix s) |

- Matching is **header-based, never positional**: any column order, missing optional columns,
  and extra/unknown columns (surfaced as "not imported") are all safe. Headers are matched
  case-insensitively after trimming; duplicate headers resolve to the first occurrence and the
  duplicate is reported.
- Firefox has **no title column**: the item title is derived — URL host → username → neutral
  `Imported <row>` label (the domain model requires a non-empty title). Nothing else is
  invented: missing notes stay empty, missing timestamps produce no provenance value.
- `guid` identifies the record inside Firefox only. It is **never** used as a vault item
  identity — vault items keep their own UUIDs.
- Timestamps are Firefox `PRTime` (microseconds since the Unix epoch); only values that
  actually parse as positive numbers are carried, divided to whole seconds, and stored under
  the provenance key — the item's own CreatedAt/UpdatedAt remain vault-owned.
- Rows whose URL is not http(s)-absolute or a bare host are marked invalid with a per-row
  reason; values are stored as-is, never interpreted as commands, and never requested.
- A file without any Firefox marker column is treated as a Chrome export (or rejected as
  unknown if it has neither schema) — Firefox exports always carry the marker columns, so the
  wizard's Firefox entry point refuses Chrome files with a "wrong browser" error and vice
  versa.
