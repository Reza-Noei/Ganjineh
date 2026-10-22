# Clipboard Security — as built (Phase 3)

## Behavior

- Copy username / copy password → `ClipboardService.TryCopyWithExpiry`: WPF clipboard set, then a
  delayed clear at the configured TTL (default **30 s**, clamp 10–120 s, stored in settings).
- **Ownership-safe clear**: after the delay the clipboard is cleared **only if** its current text
  still equals exactly what we copied. Content the user copied in the meantime is never touched.
- Clipboard access failures (clipboard locked by another process, RDP quirks) are caught and
  surfaced as a localized status message (`Status.ClipboardFailed`) — no crash, no secret in the
  message. A clear that races a busy clipboard is silently skipped (the value simply remains).
- Copy events show status-bar feedback without ever echoing the secret itself.

## Residual risks (documented, not solvable in-app)

1. **Windows Clipboard History (Win+V)** and cloud-synced clipboard may retain copied values
   beyond our TTL. The UI carries the warning string `Warning.ClipboardHistory`; Settings will
   surface guidance to disable history (release checklist).
2. Third-party clipboard managers can capture everything regardless of our clear.
3. Between copy and TTL, any process running as the user can read the clipboard (standard
   Windows trust model).

These are accepted residual risks (threat-model R-8) — the product warns; it cannot control the OS.
