# Portable Edition — as built (Phase 7)

## Layout

```
Ganjineh-Portable/
    Ganjineh.exe          (or Ganjineh.dll + runtime files)
    Data/                 (portable root — all state under here)
        settings.json
        vaults.json
        Vaults/           (default vault storage)
        Backups/          (default backup storage)
```

## Detection

A `Data` directory beside the executable signals Portable mode. Otherwise the app runs in
Installer mode (per-user AppData). Detection happens at startup in
`ApplicationDistributionContext.Resolve` — no mode toggle exists at runtime.

## Constraints and mitigations

- **Read-only media**: the app catches write failures and shows a clear error rather than
  silently falling back to an unexpected location.
- **Removable storage**: Windows may disconnect USB drives at any time; SQLite's WAL + FULL
  sync minimizes corruption on sudden removal, and the atomic header write protocol prevents
  partial vault writes.
- **No admin privileges required** for normal operation.
- **No registry keys** are written in Portable mode.
