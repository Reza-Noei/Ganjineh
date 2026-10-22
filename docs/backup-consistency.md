# Backup Consistency — as built (Phase 6)

## Database snapshot

- `VACUUM INTO` after `wal_checkpoint(TRUNCATE)` produces a consistent, committed, encrypted
  snapshot — never a blind file copy, no WAL/journal ambiguity.
- The snapshot is validated (`VaultDatabase.Open` + integrity_check + identity) during
  Cryptographic verification and Rehearsal, and on every restore.

## Attachment coordination

1. Boundary established: the DB snapshot is taken first — it defines the attachment set.
2. Attachment rows are read from the snapshot connection.
3. Each referenced blob is read and hashed (SHA-256 into the authenticated manifest).
4. Missing blob → backup fails with `Backup.MissingAttachment` (no partial backup declared).
5. Blobs are copied in their already-encrypted representation (never decrypted/re-encrypted).
6. Cryptographic integrity is verified by the package AEAD + manifest hashes on restore.
7. The write-lock window is bounded by the copy; no long-running locks are held.

## Failure handling

- Create writes to `<dest>.part` and atomically renames only after the full write; crashes leave
  at most a `.part` (cleaned on the next backup) — never a partial `.gvbak` presented as done.
- Restore stages into an isolated directory; the destination vault is registered only after
  validation; rollback = delete the staging directory.
