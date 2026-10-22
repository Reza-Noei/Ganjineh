# Session Lifecycle — as built (Phase 3)

## One active session

`VaultSessionManager` holds at most one unlocked `VaultSession` plus a monotonically increasing
`Generation`. Every vault switch or lock bumps the generation and raises `SessionChanged`.

## Boundaries enforced

- Credential operations require `session.State == Unlocked`; otherwise
  `StaleSessionException` (→ localized error / workspace lock path). A locked vault cannot serve
  credential reads, writes, or search-index builds (tested).
- `Activate(new)` locks the previous session first — "lock when switching vaults".
- In-flight async operations capture the session object, not the manager: if the session is locked
  mid-flight, the next service call throws `StaleSessionException`; UI paths catch it, wipe state,
  and route to the picker. Results from a previous generation never repopulate the workspace
  because `SessionChanged` clears list/details/index/search synchronously.
- `Lock` closes the DB (checkpoint → dispose), zeroizes the keyring, nulls references, and clears
  UI-bound detail/editor/delete state (tested via test accessors and the FlaUI flow).
- App exit: `VaultSessionManager.Dispose` → `LockCurrent`; WPF `ShutdownMode=OnMainWindowClose`.

## Auto-lock

`AutoLockService`: DispatcherTimer (15 s tick) compares idle time to `AppSettings.AutoLockMinutes`
(default 10; 0 = off). Any UI input (`InputManager.Current.PreProcessInput`) refreshes the idle
stamp. `SystemEvents.SessionSwitch (SessionLock)` requests an immediate lock. The lock request
routes through the AppViewModel to `ShowPicker()` — which locks, wipes, and re-lists vaults.

## Password handling note (documented deviation)

WPF `PasswordBox` cannot be data-bound without exposing the password as a plain string property.
Instead, the shell's code-behind copies the box content into `char[]` and hands it to the
view-model methods (`SetPasswords`, `EditorPasswordHandoff`), which zeroize after use. This keeps
plaintext passwords out of bindable/global state at the cost of a small, deliberate MVVM
compromise in code-behind — reviewed and accepted for v1.
