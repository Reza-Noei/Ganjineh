# Item History — as built (Phase 4)

- Every meaningful update appends a new `item_versions` row (same-transaction as the item
  update) containing the previous payload envelope; version numbers are 1-based and monotonic.
- **Retention:** configurable per-item cap (default 50, service parameter); the oldest versions
  are pruned after each save. Unbounded growth is prevented.
- **Restore:** `RestoreVersionAsync(id, version)` re-seals the historical payload and appends it
  as a NEW version — the current version remains in history; nothing is destroyed.
- History rows are full payload envelopes: historical passwords/secrets are as protected as
  current ones (same AEAD, same key). They are never shown unmasked in the history list (the
  list shows version + timestamp only); restoring a version brings its secrets into the normal
  (masked) detail flow.
- **User-facing caveat (must stay in product docs):** changing a password does not remove older
  values from encrypted history or from backups made earlier. Clearing history explicitly
  (prune) is the only in-app removal; backups keep their own copies.
- Integrity: history integrity is enforced transactionally — an update+history append commit or
  rolls back together (transaction rollback covered by tests).
