# Backup Verification — as built (Phase 6)

## Levels

| Level | What it proves | Requires |
|-------|----------------|----------|
| Structural | Magic/version/IDs parse; metaLen + packageLen tile the file exactly (truncation/appending detected); header parses | Nothing |
| Cryptographic | Meta AEAD authenticates; chunk AEAD authenticates (per-chunk, whole package streamed); manifest hashes match | Unlocked session (backup_wrap_key) |
| Rehearsal | Full extract into an isolated temp dir + real `VaultDatabase.Open` + integrity_check + item/attachment counts vs manifest | Unlocked session |

Each result carries the level, timestamp, and vault ID. A successful file copy is explicitly NOT
treated as verification — the Backup Center records the level with the timestamp.
