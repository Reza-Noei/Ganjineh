# Secure Settings — as built (Phase 7)

## Separation

| Category | Storage | Contents |
|----------|---------|----------|
| Non-sensitive preferences | `settings.json` (plain JSON, AppData or Portable\Data) | theme, language, window size, distribution mode, clipboard TTL, auto-lock policy |
| Security policy | same file (non-secret configuration values only) | auto-lock minutes, lock-on-suspend/minimize, clipboard timeout seconds |
| Vault-specific encrypted settings | inside the vault DB (envelope, item_key) | vault display name, per-vault preferences |

## Never stored

Master password, Recovery Key, VMK, item keys, TOTP secrets, saved credentials.

## Versioning and corruption

`settings.json` carries a `Version` field. Unknown versions are rejected (defaults loaded).
Corrupt JSON falls back to defaults (tested). Security-relevant values (auto-lock minutes,
clipboard TTL) are range-clamped on load.

## Validation

Auto-lock minutes: 1–60 or 0 (disabled). Clipboard TTL: 10–120 seconds. Invalid values are
clamped, not rejected (defensive against manual file editing).
