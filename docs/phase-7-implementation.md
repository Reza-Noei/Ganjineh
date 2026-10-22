# Phase 7 Implementation — Windows Integration, Installer, Portable & Secure Distribution

Status: Implemented and tested (phase-7-test-results.md) | 2026-10-22

## Distribution mode abstraction

`IApplicationDistributionContext` in Application.Abstractions resolves Portable vs Installer at
startup by detecting a `Data` directory beside the executable (ADR-006). ViewModels receive the
context via DI; no scattered mode checks exist.

- **Installer mode**: config in `%APPDATA%\Ganjineh`, vaults default to
  `%USERPROFILE%\Documents\Ganjineh\Vaults`.
- **Portable mode**: everything under `<exe>\Data` (config, vaults, backups, registry).
  Relative paths survive moving the directory to another computer.

## Single instance

`SingleInstanceLock` uses a named `Mutex` (crash-safe — abandoned mutex is destroyed by the OS)
plus a named `EventWaitHandle` for activation signaling. The second instance signals the
primary to show its window, then exits.

## System tray

`TrayIconService` (P/Invoke Shell_NotifyIcon) provides: Show, Lock Vault, Settings, Exit.
Configurable minimize-to-tray (setting: `MinimizeToTray`, default true). The tray tooltip
shows only the vault name (or "locked") — no secrets.

## Windows session events

`SystemEvents.SessionSwitch` handles:
- SessionLock → immediate lock
- SessionUnlock → no action (user must re-authenticate)
Remote Desktop disconnect maps to session lock; suspend/resume refreshes the idle timer.

## Advanced auto-lock

`AutoLockService` uses monotonic elapsed-time measurement (Environment.TickCount64 — not
DateTime, which is vulnerable to clock changes). Lock-on-suspend and lock-on-minimize are
configurable; lock-on-exit is always active. A pending UI operation cannot bypass the lock —
the lock path cancels the workspace state machine directly.

## Settings

`AppSettings` is versioned (v1), JSON-serialized, and validated on load. Corrupt settings fall
back to defaults (tested). Security-relevant settings (auto-lock, clipboard TTL) are separated
from appearance settings in the settings screen. No secrets are stored.

## Offline update foundation

`UpdateVerificationService` verifies Ed25519-signed update manifests against an embedded public
key. The signing key is held offline by the release engineer. Unsigned or tampered packages are
rejected. No download logic — the user selects the update package file.
