# Full Restore — as built (Phase 6)

## Flow

1. Select `.gvbak`; Structural pre-check (format + length tiling).
2. Unlock prompt: Master Password or Recovery Key (the backup embeds both wraps).
3. `OpenBackupAsync`: meta AEAD + chunk AEAD over the whole package → decrypted container.
4. Staging directory `<uuid>.pvault`: header.bin (backup copy), vault.db, attachments/.
5. Validation: `VaultDatabase.Open` + integrity_check + identity vs embedded header +
   item/attachment counts vs manifest (live+trashed must equal ItemCount).
6. Register the restored vault; open a session.

## Safety

- The destination is a fresh isolated directory; an existing `<uuid>.pvault` at the destination
  → `Restore.DestinationExists`, no overwrite.
- Any failure between staging and registration → the staging directory is deleted; the original
  vault and other vaults are untouched.
- After restore, the Master Password reset and Recovery Key rotation flows apply unchanged
  (Phase 2 mechanisms on the restored header).
