# Phase 6 Implementation — Encrypted Backup, Restore, Import/Export & Disaster Recovery

Status: Implemented and tested (phase-6-test-results.md) | 2026-10-22

## What was built

1. **`.gvbak` v1 backup format** — versioned, authenticated, portable (encrypted-backup-format.md).
2. **`IVaultBackupService`** — full encrypted backup (consistent SQLCipher snapshot via
   `VACUUM INTO` + attachment blobs in their encrypted representation, streamed through the
   ChunkedAead package), atomic finalize via part-file + rename, retention pruning.
3. **Backup verification** — three levels: Structural (plaintext length-consistency), 
   Cryptographic (meta AEAD + chunk AEAD with the session's backup-wrap key), Rehearsal
   (full extract into an isolated temp dir + real `VaultDatabase.Open` + integrity check).
4. **Full restore** — staged into an isolated directory, validated (DB identity + integrity +
   item/attachment counts vs manifest), registered only after success; refuses to overwrite an
   existing vault directory.
5. **Selective restore** — decrypt-with-source-keys → re-encrypt-with-target-keys per item and
   attachment; conflict policies Skip / CreateCopy / ReplaceExisting.
6. **Recovery integration** — the backup's embedded header carries the password AND recovery
   wraps, so a backup unlocks with either credential; master-password reset works on a restored
   vault via the existing Phase 2 mechanism.
7. **Import** — generic CSV and Bitwarden JSON adapters with safe parsing, preview, duplicate
   detection, and formula-injection-safe field handling.
8. **Export** — encrypted (same `.gvbak`) and plaintext CSV/JSON with explicit confirmation,
   UTF-8, CSV escaping and formula-injection defenses.
9. **Backup Center UI** — create/verify/restore/import/export flows in the Persian RTL
   workspace, plus per-vault backup settings (mode, directory, retention).

## Key fixes made during hardening

- The chunked-package reader never advanced past chunk authentication tags (framing bug found by
  the integration tests); fixed and covered by the framing tests.
- The manifest's `DbSha256` was the full-snapshot hex instead of the SHA-256 digest (missing
  hash step); fixed — the digest is what the manifest authenticates.
- Meta AAD covers the fixed header + header copy; package region arithmetic fixed to include the
  8-byte pkgLen prefix.
- `UnlockHeader`'s recovery fallback maps non-PMRK1 credentials to the generic authentication
  failure (no format oracle).

## Not in this phase (per contract §15)

Cloud sync, browser extension, remote providers, Windows Hello, Auto-Type, installer packaging,
online breach monitoring. Automatic background scheduling runs only while the vault is unlocked
(documented in backup-retention.md).
