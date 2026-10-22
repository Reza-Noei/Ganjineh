# HOTP Authenticator — as built (Phase 5)

- RFC 4226 engine shared with TOTP (`OtpEngine.Hotp`); all ten Appendix D vectors pass
  (SHA-1, 6 digits, secret `12345678901234567890`).
- Counter-based semantics are explained in the UI where a HOTP item is configured: codes change
  only when the counter advances, not with time.
- Counter advancement is transactional: the new counter is persisted through the normal
  item-update envelope save (single transaction with the payload); ViewModel refreshes never
  advance it. Invalid secrets (≥8-byte rule) and digit ranges are rejected before computation.
- v1 scope: engine + data keys + tests; a dedicated HOTP item UI is deferred (contract asked for
  correct implementation, manual generation and transactional advancement, all delivered at the
  service level).
