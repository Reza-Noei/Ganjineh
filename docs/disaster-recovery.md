# Disaster Recovery — as built (Phase 6)

## Scenarios

### A — New computer
Backup + Master Password (or Recovery Key) → `RestoreAsync` stages into `<dest>/<uuid>.pvault`,
validates (identity, integrity, counts), registers, opens. Tested by
`Full_Restore_On_Fresh_Registry_Portably_Unlocks` (fresh registry = fresh machine).

### B — Forgotten Master Password
Current vault or backup + Recovery Key → restore/unlock with the Recovery Key →
`ResetMasterPasswordWithRecoveryKeyAsync` (fresh KDF salt, VMK preserved, Phase 2 mechanism).
Tested by `Recovery_Key_Unlocks_Backup_And_Resets_Master_Password`.

### C — Corrupted working vault
Verified backup + valid credential → restore to a NEW location (the corrupted vault directory is
never touched). Validation failure → staging discarded, original untouched.

### D — Missing attachments
`CreateAsync` fails with `Backup.MissingAttachment` (never declares success);
`RestoreAsync` fails with `Restore.Incomplete` when counts mismatch. A documented
partial-recovery option is deferred — no silent partial restores.

### E — Lost Master Password AND Recovery Key
Not recoverable. No backdoor, no override, no hidden mechanism. The UI states this during
vault creation and in the Recovery Key documentation.

## Rehearsal results

See phase-6-test-results.md — the disaster-recovery matrix (machine available/unavailable ×
credentials × backup validity × attachment completeness) is exercised by the Phase 6
integration tests; actual restore rehearsals run the real extraction + `VaultDatabase.Open` +
integrity check path.
