# Reliability Test Report — Phase 8 Fault Injection

Date: 2026-10-22 | Commit: `4bb671e` (Phase 7) | All Phase 8 adversarial tests: 8/8 green

## Fault injection scenarios

| Scenario | Injection point | Result |
|----------|----------------|--------|
| Header bit-flip (2000 cases, deterministic seed) | HeaderCodec.Parse | All mutations → typed errors or pass; no crash, no corruption |
| Backup creation interrupted before part→final rename | Inside CreateAsync (before File.Move) | Original vault intact; .part + .pkg cleaned by finally |
| Restore with wrong password | RestoreAsync UnlockHeader | Restored vault dir not created; error reported; original untouched |
| Chunk tag tamper | .pmblob byte flip | Export rejects; no partial output written |
| Truncated backup (tail −64 bytes) | OpenBackupAsync | Structural length-consistency check fires (Backup.Truncated) |
| Locked-session item operations | Post-lock CRUD | All service calls throw StaleSessionException |
| Non-PMRK1 credential in recovery fallback | UnlockHeader | Mapped to generic CryptoAuthenticationException (no format oracle) |
| Malicious KDF params (memory/iterations/parallelism) | KdfParameters.Validate | Rejected with typed InvalidKdfParametersException |
| Nonce uniqueness (10 000 AEAD encryptions) | AesGcm.Encrypt nonce | Zero collisions |

## Consistency guarantees verified

- Header + DB + attachments are written atomically via temp-file → fsync → rename.
- The original vault remains fully usable after a failed backup, restore, or verify.
- Staged restore directories are cleaned on failure; the original vault directory is untouched.
- Vault deletion preserves user data directories; only the vault-specific `.pvault` dir is removed.

## Limitations

- Process kill (simulated crash) during VACUUM INTO: SQLite's WAL mechanism ensures the
  original DB remains consistent; the incomplete snapshot is orphaned but harmless.
- Concurrent access by two processes on the same vault file: SQLite's file locking prevents
  silent corruption; the single-instance mutex prevents two app instances at the UI level.
- Simulated USB removal: the OS may buffer writes; Windows drive removal without safe eject
  is outside the application's control. WAL + FULL sync minimizes data loss window.
