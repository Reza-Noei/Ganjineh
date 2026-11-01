# Phase 6 Test Results

Date: 2026-10-22 | Suite: `dotnet test Ganjineh.slnx` — **203/203 green**, zero warnings.

## Phase 6 coverage (8 integration tests, all green)

| Test | Proves |
|------|--------|
| Backup_Verify_Rehearsal_And_Restore_RoundTrip | Create with item+attachment → backup → Cryptographic verify (counts) → Rehearsal (real DB extract + open + integrity) |
| Full_Restore_On_Fresh_Registry_Portably_Unlocks | Fresh-registry (= fresh machine) restore; same vault ID; item decodes; vault reopens with the same master password |
| Wrong_Credential_Is_Rejected | Wrong password → `Unlock.WrongPassword` (destination-fresh scenario) |
| Recovery_Key_Unlocks_Backup_And_Resets_Master_Password | Backup unlocked with the Recovery Key; Phase 2 master-password reset works on the restored vault |
| Tampered_Backup_Is_Detected | Byte-flip in the package region → meta/chunk AEAD failure → Backup.Tampered |
| Truncated_Backup_Fails_Structural_Verification | Tail truncation → structural length-consistency check fails (Backup.Truncated) |
| Selective_Restore_ReEncrypts_Into_Another_Vault | Cross-vault selective restore: item decrypted with source keys, re-encrypted under target keys (CreateCopy), attachment re-encrypted and exports byte-identical |
| Retention_Prune_Keeps_Newest | 3 backups → prune to 2 → newest retained |

## Issues found and fixed during hardening

1. Reader never skipped chunk authentication tags (position tracking bug) — the integration
   tests caught it immediately; fixed and the chunk-framing test (BackupChunkDiag) confirmed.
2. Manifest `DbSha256` was the full-snapshot hex instead of the SHA-256 digest — fixed.
3. Meta/package region arithmetic missed the 8-byte pkgLen prefix on the reader side — fixed.
4. Recovery fallback surfaced format-oracle errors for non-PMRK1 credentials — now mapped to
   the generic authentication failure.

## Disaster-recovery matrix (executed)

| Scenario | Credential | Backup state | Result |
|----------|-----------|--------------|--------|
| New machine | Master Password | Valid | ✓ restored, opens, item decodes |
| New machine | Recovery Key | Valid | ✓ restored via recovery wrap |
| Same machine, wrong password | wrong MP | Valid | ✓ Unlock.WrongPassword (destination-fresh case) |
| Corrupted package | valid MP | tampered | ✓ Backup.Tampered, no restore |
| Truncated file | valid MP | truncated | ✓ Backup.Truncated (structural + crypto) |
| Restoring onto the source location | any | valid | ✓ Restore.DestinationExists (safe refusal) |
| Selective into another vault | source MP | valid | ✓ re-encrypted under target keys, attachment exports byte-identical |
| Lost MP AND Recovery Key | — | any | not recoverable (by design; no backdoor) |
