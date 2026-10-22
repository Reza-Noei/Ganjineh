# Security Analysis Privacy — as built (Phase 5)

What leaves the machine in Phase 5: **nothing.** The strength estimator (zxcvbn-core), the OTP
engine and the security analyzer are fully offline; there is no network code in any of their
paths and no telemetry anywhere in the product.

- Analysis input (decrypted payloads) exists only inside the active vault session; findings are
  in-memory DTOs without password plaintext, dropped on lock/switch (session-boundary wipe).
- Reuse detection uses ephemeral in-memory fingerprints; they are never written and are as
  short-lived as the analysis itself.
- No unsalted password hashes, no plaintext comparison indexes, no analysis caches on disk
  (sentinel-scanned by tests).
- Clipboard copies of generated values/TOTP codes use the Phase 3 TTL service; the Windows
  Clipboard History caveat documented there applies here too.
- Windows notifications are not used for findings — no sensitive metadata can leak through them.

## Limitations

- In-memory plaintext is inherent to a working password manager while unlocked (threat R-1).
- The zxcvbn dictionaries are English-centric; Persian passwords are length/entropy-scored with
  reduced pattern awareness (documented; a curated Persian dictionary could improve this later).
- The aggregate score is a local-hygiene heuristic — it says nothing about server-side breaches.
