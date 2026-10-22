# Cryptographic Audit — Phase 8

## Implementation summary

| Primitive | Library | Verified by | Status |
|-----------|---------|-------------|--------|
| AES-256-GCM (envelopes, wrapping, backups, attachments) | .NET BCL `AesGcm` | NIST/McGrew GCM KATs, cross-implementation round-trip | ✅ |
| Argon2id (password KDF) | NSec 26.4.0 (libsodium 1.0.22) | Cross-validated vs BouncyCastle 2.7.0 (4 param sets incl. Unicode) | ✅ |
| HKDF-SHA-256 | .NET BCL `HKDF` | RFC 5869 A.1–A.3 + API≡primitive equivalence | ✅ |
| Ed25519 (update manifests) | NSec 26.4.0 | Sign/verify round-trip, tamper, wrong-key | ✅ |
| Ed25519 (test cross-validation) | .NET `Ed25519.Create()` | Independent implementation confirms NSec output | ✅ |
| Base32 (recovery codec) | In-house over NSec | RFC 4648 vectors, Crockford homoglyph tolerance | ✅ |
| CRC-32 (recovery checksum) | In-house | "123456789" → 0xCBF43926 | ✅ |
| TOTP (RFC 6238) | In-house over BCL HMACSHA | RFC 6238 Appendix B (SHA-1/256/512 × 2 timestamps) | ✅ |
| HOTP (RFC 4226) | In-house over BCL HMACSHA1 | RFC 4226 Appendix D (counters 0–9) | ✅ |
| ChunkedAead (backup/attachment packages) | NSec Ed25519 + .NET AesGcm | Multi-chunk round-trip, reorder/truncation/tamper rejection | ✅ |

## Reviewed properties

- **Nonce uniqueness**: 10 000 AEAD encryptions under same key → zero collisions (statistical).
- **Key separation**: HKDF domain labels prevent cross-purpose key reuse (tested).
- **Vault binding**: salt = VaultID in every HKDF call prevents cross-vault key reuse (tested).
- **Password encoding**: raw UTF-8, no normalization (tested with Persian, emoji, ZWNJ).
- **Auth tag validation**: decryption never returns unauthenticated plaintext (tested).

## Identified deviations (all documented)

1. Engine parallelism p=1 (NSec/libsodium constraint) — documented in ADR-003 revision.
2. `DbSha256` bug (full hex instead of SHA-256 digest) — fixed in Phase 6 hardening.
3. Chunk tag-skip bug in backup reader — fixed in Phase 6 hardening.

## Dependencies

| Package | Version | License | Maintenance |
|---------|---------|---------|-------------|
| NSec.Cryptography | 26.4.0 | MIT | Active (Klaus Hartke) |
| libsodium (native) | 1.0.22 | ISC | Active (jedisct1) |
| SQLitePCLRaw.* (e_sqlcipher) | 2.1.11 | Apache 2.0 | Active (ericsink) |
| zxcvbn-core | 7.0.92 | MIT | Active |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | Active (Microsoft) |
| Microsoft.Extensions.DependencyInjection | 10.0.13 | MIT | Active (Microsoft) |

No known CVEs in any of these versions at time of review.
