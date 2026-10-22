# Selective Restore — as built (Phase 6)

## Flow

1. The backup is staged and opened with its own credentials (isolated temp dir + temp registry).
2. The user previews item metadata from the staged vault and selects items.
3. Each selected item: decrypted with the STAGED vault's keys → re-encrypted with the TARGET
   session's keys → created in the target vault. Ciphertext is never copied between vaults.
4. Attachments: exported (decrypted) from the staged blob → re-encrypted into the target under
   the target's attach key, bound to the restored item.
5. Conflict policies: Skip / CreateCopy / ReplaceExisting (Replace updates in place, preserving
   the target item's identity; the policy is explicit per restore).

## Limitations (v1, documented)

- Restored items land at the vault root; folder/tag re-creation by name is a documented
  simplification (folder/tag name collision handling is Phase 7+ UI work).
- Item history is not carried by selective restore (full-history restore = full vault restore).
