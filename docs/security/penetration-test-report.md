# Penetration Test Report — Phase 8 Internal Review

## Scope

Adversarial review of all security-critical code paths: header parsing, key derivation, AEAD
envelope decryption, key wrapping, backup packaging/extraction, recovery fallback, session
boundary enforcement, and at-rest storage. The review was conducted by the same AI agent that
wrote the implementation — **this is not an independent security audit** (see §24).

## Test vectors

| Attack | Method | Result |
|--------|--------|--------|
| Header bit-flip fuzz | 2000 deterministic single-bit mutations of a valid header | All produced typed CryptoFormatException/UnsupportedFormatException or passed auth; no crash, no corruption |
| Nonce uniqueness | 10 000 AEAD encryptions under same key | Zero collisions |
| Wrong-password unlock | Wrong master password through full unlock path | Generic CryptoAuthenticationException with empty message (no oracle) |
| Wrong recovery key | Valid PMRK1 from different vault through recovery path | Generic CryptoAuthenticationException |
| Non-PMRK1 recovery fallback | Non-PMRK1 credential through recovery path | Mapped to generic CryptoAuthenticationException (no format oracle) |
| Chunk 1+ nonce/AAD mismatch | Reader position advanced without tag skip (found bug F-01) | Fixed; regression test added |
| Attachment tag tamper | Last byte of .pmblob flipped | Export rejected |
| Backup package tamper | Byte-flip in package region | Backup.Tampered; no restore performed |
| Truncated backup | Tail truncated by 64 bytes | Structural length-consistency check fires (Backup.Truncated) |
| Cross-vault selective restore | Item from vault A restored into vault B | Re-encrypted under B's keys; decrypts correctly; A's data unaffected |
| At-rest sentinel scan | Plaintext sentinel in header/DB/WAL/shm/registry | Zero occurrences across all vault-related files |

## Findings discovered and fixed during review

Four findings were discovered and fixed during this phase (F-01 through F-04 in
security-findings.md). All are verified by regression tests.

## Residual risks

See residual-risks.md and the threat model. Key accepted risks: memory scraping while
unlocked (R-1), whole-file rollback (R-2), old backup + old recovery key (R-3), no secure
SSD erasure (R-6).
