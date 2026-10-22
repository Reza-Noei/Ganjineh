# Clipboard Hardening — as built (Phase 5–7)

See clipboard-security.md (Phase 3) for the base TTL mechanism. Phase 7 additions:

- Copy TOTP/HOTP codes through the same TTL path
- Copy custom concealed field values (same rules as password)
- Clear-on-shutdown: the clipboard is cleared (if owned) during application exit
- Ownership check: clear only if the clipboard still holds exactly what we copied

## Limitations (documented, not solvable)

Windows Clipboard History (Win+V) and third-party clipboard managers may retain copied data
beyond the TTL. The app clears its own clipboard writes but cannot control other applications.
The settings screen carries the warning; users who need stronger guarantees should disable
Clipboard History in Windows Settings.
