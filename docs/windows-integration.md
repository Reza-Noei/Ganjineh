# Windows Integration — as built (Phase 7)

## System Tray

Implemented via P/Invoke `Shell_NotifyIcon` (no third-party dependency). Context menu: Show,
Lock Vault, Settings, Exit. Double-click shows the window. Minimize-to-tray is configurable
(default: true). The tooltip shows only the vault name or "locked" — never secrets.

## Session events

`SystemEvents.SessionSwitch`:
- `SessionLock` → immediate lock
- `SessionUnlock` → no auto-action (user must re-authenticate)
- `RemoteDisconnect` → treated as session lock

`SystemEvents.PowerModeChanged`:
- `Suspend` → immediate lock
- `Resume` → idle timer resets

Event subscriptions are registered at startup and unregistered at shutdown.

## Auto-Lock

Monotonic measurement via `Environment.TickCount64` (immune to system clock changes). Idle
detection resets on keyboard/mouse input via `InputManager.Current.PreProcessInput`. Policies:
1/5/10/15/30 minutes or Never (with a security warning). Lock-on-minimize is optional.
The lock path cancels workspace operations directly — a pending async operation cannot
repopulate state after the lock.
