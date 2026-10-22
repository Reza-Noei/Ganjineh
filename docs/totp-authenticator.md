# TOTP / HOTP Authenticator — as built (Phase 5)

## Algorithms

- **HOTP (RFC 4226):** HMAC-SHA-1/256/512 over an 8-byte big-endian counter; dynamic truncation
  per §5.3; digits 6–8. All ten Appendix D vectors pass.
- **TOTP (RFC 6238):** HOTP over UTC-Unix-time/period; period 1–3600 s (default 30); SHA-1/256/512
  with the per-algorithm RFC secret lengths. Appendix B vectors (T=59, T=1111111109, all three
  algorithms) pass. Boundary behavior (constant inside window, roll at exact boundary) tested.
- Clock: explicit `DateTimeOffset` parameter — deterministic in tests; drift is visible to the
  user as `SecondsRemaining`.

## Storage & binding

TOTP configuration lives inside the Login item payload envelope (`totpSecret` Base32, issuer,
account, algorithm, digits, period) — encrypted by the same item envelope as the password,
bound to Vault+item by the envelope AAD. Nothing touches settings or logs. HOTP counters update
only through item saves (transactional with the payload; refresh never advances them).

## Import

Manual Base32 entry and `otpauth://totp/…` URIs (validated: scheme, Base32 secret ≥8 B/≤200
chars, algorithm whitelist, digits 6–8, period 1–3600, URI length cap). QR-image decode is
deferred (would require a bundled decoding dependency).

## UI behavior

Details pane (login items with a stored authenticator): code, seconds-remaining, copy (through
the TTL clipboard service). A 1 Hz DispatcherTimer refreshes only while such an item is selected;
lock/switch stops the timer and clears the code (session wipe). The factor-separation warning
("password + TOTP seed in one item reduces separation") is displayed under the code.

## Limitations

- No QR decode, no webcam, no steam-guard-style proprietary URIs.
- Clock drift is displayed (seconds remaining) but no drift-correction/resync window is applied —
  users should fix their system clock (documented guidance).
