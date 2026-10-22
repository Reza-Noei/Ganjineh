# Backup Key Management — as built (Phase 6)

## Credentials per backup type

| Backup type | Unlocked by | Notes |
|-------------|------------|-------|
| Full vault backup (`.gvbak` v1, default) | Source vault's **Master Password** or **Recovery Key** (wraps carried in the embedded header copy) | Faithful snapshot — same VMK, same credentials as the live vault |
| Optional future Backup-Password backup | Dedicated backup password (Argon2id + fresh salt + AEAD wrap, clearly labeled) | NOT implemented in v1; design reserved in the format (meta version bump) |

The Master Password is never silently treated as a Backup Password and vice versa — the UI
labels each unlock prompt explicitly.

## VMK lifecycle in backups

- The backup embeds the source header's password+recovery wraps — the VMK itself is never stored
  plaintext. `backup_wrap_key` (HKDF of VMK) authenticates meta + package.
- Changing the live vault's master password or rotating its Recovery Key does **not** revoke
  older backups: they carry the older, still-valid wraps. This is stated in the UI and docs
  (contract §3).
- Recovery Key rotation compatibility: a backup restored with an old Recovery Key can be
  unlocked, then the master password reset via the carried recovery wrap — the Phase 2 reset
  flow applies unchanged.
