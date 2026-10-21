# Cryptographic Test Results — Phase 2

Date: 2026-10-22 | Machine: Windows 11 Enterprise 22621, dev-class x64, .NET SDK 10.0.400
Suite: `dotnet test PasswordManager.slnx` — **103/103 green** (zero warnings, TreatWarningsAsErrors).

## Known-Answer and Cross-Validation Results

| Area | Vectors / method | Result |
|------|------------------|--------|
| AES-256-GCM | McGrew/Viega TC14 (empty PT), TC15 (16 B), NIST 256-bit+AAD case (Botan gcm.vec mirror: tag `76fc6ece…bb2d551b`) | PASS |
| AES-256-GCM service | wrong key/AAD, ct/tag tamper, truncation, bad version/algo/domain, nonce uniqueness | PASS |
| HKDF-SHA-256 | RFC 5869 A.1, A.2 (full 164-hex OKM), A.3; API≡primitive equivalence with our info format | PASS |
| Argon2id | **Cross-validation vs BouncyCastle 2.7.0** (independent managed implementation), 4 parameter sets incl. Persian/emoji/ZWNJ passwords | PASS |
| Argon2id | Pinned golden vector (m=128 MiB baseline path) + determinism; NSec parameter-rejection guard | PASS |
| CRC-32 | "123456789" → 0xCBF43926 | PASS |
| Recovery codec | round-trip, case/space/homoglyph tolerance, checksum rejection, prefix/length rejection | PASS |
| Header codec | round-trip all fields, reserved-zero layout, MAC verify/tamper | PASS |

**Unreachable vectors (documented):** RFC 9106 reference vectors need associated-data/secret
inputs that libsodium's `crypto_pwhash` does not expose; the phc-winner-argon2 published vectors
use an 8-byte salt, below libsodium's enforced 16-byte salt. The BouncyCastle cross-validation is
the independent-implementation check in their place.

## Benchmark (Q2 closed) — Argon2id p=1, median of 20 (p95 in parens)

| m | t=2 | t=3 |
|---|-----|-----|
| 32 MiB | — | 39 ms (45) |
| 64 MiB | 61 ms (71) | 85 ms (103) |
| 128 MiB | — | **172 ms (183)** |

Per the ADR-003 procedure (raise m while p95 < 0.3 s, ceiling 128 MiB), the shipped baseline is
**m=128 MiB, t=3, p=1** — 172 ms on this dev machine; projected 0.35–0.7 s on min-spec hardware,
inside the 0.3–1.0 s unlock band. Benchmark test: `--filter "Category=Benchmark"`.

## Adversarial / Security Test Results

| ID | Scenario | Result |
|----|----------|--------|
| ST-SEC-1 | Wrong password / wrong recovery / swapped wraps → identical generic `CryptoAuthenticationException`, empty message | PASS |
| ST-SEC-2 | Recovery unlock success/failure; rotation revocation; recovery-based MP reset | PASS |
| ST-SEC-3 | Header tamper (wrap/salt/params/MAC regions) → auth or format failure; 2 000-case deterministic bit-flip fuzz → typed errors only, no crashes | PASS |
| ST-DB-1/2 | DB not ordinary SQLite at rest; no sentinel secrets in header/DB/WAL/shm/registry | PASS |
| ST-DB-4 | Registry carries only vaultId/path/timestamps/counter (schema assertion via plaintext scan) | PASS |
| ST-INT-1/2 | Vault isolation: distinct IDs, DB bytes, keys; A's recovery key cannot open B | PASS |
| Crash safety | Fault injection at both atomic-write points → previous header byte-identical and usable; stale temp cleaned on open | PASS |
| R-2 | Whole-header rollback to counter-1 snapshot → `RollbackWarning` on session | PASS |

## Residual Risks / Limitations (explicit)

1. Memory scraping of an unlocked session is out of scope for user-mode (R-1; see
   secure-memory-limitations.md). Testing does not prove absence of side-channel or
   memory-disclosure vulnerabilities.
2. Whole-file rollback is detectable only post-unlock via the registry high-water mark; a rolled
   back *database* paired with a rolled-back header on a fresh machine is undetectable (R-2).
3. Old snapshot + old Recovery Key remains openable forever (R-3).
4. Random 96-bit nonces: documented birthday bound (≈4×10⁻¹⁷ at 10⁶ envelopes/vault); attachment
   chunks (Phase 4) switch to counter-derived nonces.
5. `cipher_memory_security = ON` costs throughput; measured acceptable at Phase 2 scale, revisit
   with large attachments in Phase 4 benchmarks.
