# Crash Handling — as built (Phase 7)

## Handlers

| Handler | Scope | Behavior |
|---------|-------|----------|
| `DispatcherUnhandledException` | UI thread | Persian error dialog; exception details never shown; marked handled |
| `AppDomain.UnhandledException` | Non-UI threads + fatal | Writes a sanitized diagnostic entry to the log file; process terminates |
| `TaskScheduler.UnobservedTaskException` | Background tasks | Logged; exception suppressed (prevents process crash for non-critical background failures) |

## Sensitive data redaction

The logging pipeline uses an allowlist approach: messages are only written for log categories
explicitly enabled. Exception messages from the crypto layer are generic (`CryptoAuthenticationException` →
"Authentication failed") and never include key material, salts, ciphertext, or plaintext.
Vault/backup file paths are logged at Debug level only (not in production builds).

## Crash dumps

Windows Error Reporting (WER) LocalDumps may capture process memory while a vault is unlocked.
The application does NOT enable or disable WER programmatically — this is a system-level
configuration. Users concerned about memory exposure should disable WER LocalDumps for the
process or ensure the vault is locked before performing sensitive operations.

## Diagnostic export

The Settings screen offers a sanitized diagnostic report (app version, distribution mode, OS
version, .NET version, recent log entries with secrets redacted). The report requires explicit
user action; no automatic upload exists.
