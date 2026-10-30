# Chrome CSV Import

Supported source: the CSV file produced by Google Password Manager's **Export passwords**
feature (chrome://password-manager/settings → Export, or Settings → Autofill → Google Password
Manager → Settings → Export passwords). Windows asks for the device password before exporting.

## Recognized schema (header-based mapping)

| Header | Required | Mapped to |
|--------|----------|-----------|
| `name` | no | Item title |
| `url` | **yes** | Login URL list (first entry) |
| `username` | **yes** | `username` data key |
| `password` | **yes** | `password` data key |
| `note` | no | Item notes (description) |

- Matching is **header-based, never positional**: any column order works; extra/unknown columns
  are listed in the wizard as "not imported" and counted — never silently dropped.
- Header names are matched case-insensitively after trimming. A second occurrence of a header
  resolves to the first; the later duplicate is reported as an unsupported column.
- Aliases accepted: `url`/`website`/`login_uri`, `username`/`login_username`,
  `password`/`login_password`, `note`/`notes`.
- A file whose headers carry Firefox markers (`guid`, `httpRealm`, `formActionOrigin`,
  `timeCreated`) is refused with a "wrong browser" error pointing at the Firefox action.

## Row handling details

- Chrome exports URLs as absolute `https://…` values; they are stored as-is (trailing
  whitespace trimmed). Bare-host values (`example.com/path`) are accepted; non-http(s) schemes
  (`javascript:` etc.) mark the row invalid with a per-row reason. Values are never interpreted
  as formulas, scripts or commands, and the app never requests imported URLs.
- Missing password → imported with an empty password (the Security Center flags it later).
  A row with url, username and password all empty is counted as empty and skipped.
- The `name` column is usually the hostname; when absent, the title is derived: URL host →
  username → neutral `Imported <row>` label (the domain model requires a non-empty title).
- Per-record field caps mirror the credential model (title 200, username 320, password 1000,
  URL 2048, notes 10 000 characters); oversized rows show as invalid in the preview.
- Chrome CSVs carry no TOTP, no timestamps and no GUIDs — nothing is invented for them.
