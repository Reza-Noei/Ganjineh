# Backup Retention — as built (Phase 6)

- Modes: Disabled (default), Manual-only, Daily, Weekly. Runs only while the vault is unlocked
  (the keys must be available; no persistent unlock secret is stored — documented limitation).
- Destination: user-chosen directory; default `<vaults>/backups`.
- Retention: keep N most recent (default 5, min 1). `PruneBackups` never deletes the newest
  verified backup; deletion happens only after a replacement is fully written and verified.
- Missing destination: silently skipped, surfaced as status; unrelated files are never touched
  (only `*.gvbak` in the chosen directory).
- Deleted backup files: no secure-erasure claim (SSD remanence, R-6).
- Last successful backup timestamp per vault, shown in the Backup Center.
