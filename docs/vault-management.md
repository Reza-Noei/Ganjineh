# Vault Management — as built (Phase 3)

## Registry vs vault identity

| Data | Lives where | Sensitivity |
|------|-------------|-------------|
| Vault ID | Header + DB (`app_meta.vault_id`) + registry | Non-sensitive identifier |
| Display name | **Encrypted** in DB (`app_meta.vault_name`, envelope, AAD = VaultID‖"vault-name") | Authoritative, secret-class |
| Local label | Registry (`vaults.json`), plain text, optional | Explicitly this-device-only; the picker flags it |
| Path / last-opened / header-counter | Registry | Non-sensitive |

Pre-unlock the picker shows the local label or directory name — never a decrypted name. The
workspace header shows the decrypted name after unlock (`GetVaultName`).

## Workflows

- **Create** (`CreateVaultViewModel` → `IVaultLifecycle.CreateAsync`): name + location + password
  (+confirm, mismatch-checked) → vault created via the Phase 2 crypto path → **Recovery Key shown
  once** with copy button → transcription challenge (random 4-char group of the key, invariant
  digits) gates completion → workspace opens. Partial creation is impossible: header + DB are
  written atomically; failure leaves no vault and no registry entry.
- **Open existing**: folder picker; validates `header.bin` presence; registers and proceeds to unlock.
- **Unlock**: password path (Argon2id, ~0.2–1 s; busy indicator explains the deliberate delay) or
  Recovery Key path (PMRK1 parse + checksum → HKDF). Both reuse Phase 2 code unchanged.
- **Lock**: manual button, idle timer (default 10 min, configurable, any UI input defers), Windows
  session lock (`SystemEvents.SessionSwitch`), vault switching, and app exit. Lock closes the DB
  (WAL checkpointed), zeroizes the keyring, and the workspace wipes list/details/search/editor state.
- **Switch**: `VaultSessionManager.Activate` locks the current session first; at most one active.
- **Rename**: encrypted name + local label updated; VaultID, keys, data untouched.
- **Remove from list**: registry entry only — files remain (stated in the confirm dialog).
- **Delete**: typed-off confirmation overlay states the permanence and the SSD no-secure-erase
  caveat (R-6); deletes the `<VaultId>.pvault` directory and the registry entry. Other vaults are
  untouched (isolation tests).
