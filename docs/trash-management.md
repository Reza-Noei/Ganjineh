# Trash Management — as built (Phase 4)

- **Soft delete:** `trashed_at` set to UTC now; soft-deleted items vanish from lists, the search
  index, and attachment listing paths (filters exclude them everywhere by default).
- **Restore:** clears `trashed_at`; the item (and its attachments/history) return intact.
- **Permanent delete:** removes the item row (FK cascade removes versions and tag links) and
  deletes attachment blobs from disk. Requires the explicit confirmation overlay.
- **Empty trash:** purges items soft-deleted before the retention cutoff (configurable days,
  0–3650); returns the count; attachment blobs are removed with the rows.
- **Deletion timestamp** is shown in trash flows (`trashed_at`).

## Secure-erasure limitations (documented, never claimed otherwise)

SQLite frees pages on DELETE; the content remains in the file until reused, and on SSDs wear-
leveling means previously written ciphertext pages may persist in flash cells regardless of any
application action. Backups (`.gvbak`, Phase 6) additionally retain whatever they captured.
The deletion dialog states these limits; the product never claims secure erasure (threat R-6).
