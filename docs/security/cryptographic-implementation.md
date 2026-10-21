# Cryptographic Implementation — Phase 2 (as built)

Status: Implemented and tested | Supersedes conflicting details in Phase 0 docs where noted.

## Libraries and Versions (pinned in projects)

| Role | Package | Version | Notes |
|------|---------|---------|-------|
| Argon2id (libsodium binding) | NSec.Cryptography | 26.4.0 | MIT, author Klaus Hartke; binds libsodium `crypto_pwhash_argon2id` |
| libsodium native | libsodium (NuGet) | 1.0.22 | pulled transitively by NSec |
| AEAD / HKDF / HMAC / CSPRNG | .NET 10 BCL (`AesGcm`, `HKDF`, `HMACSHA256`, `RandomNumberGenerator`) | 10.0.x | hardware AES-NI |
| SQLCipher native | SQLitePCLRaw.lib.e_sqlcipher | 2.1.11 | SQLCipher 4.x community build |
| SQLCipher provider | SQLitePCLRaw.provider.e_sqlcipher + .core | 2.1.11 | explicit `raw.SetProvider` — see sqlcipher-integration.md |
| Test-only cross-validation | BouncyCastle.Cryptography | 2.7.0 | independent managed Argon2id; never shipped |

## Primitives and Parameters (normative)

- **Argon2id** (`KdfParameters`): baseline **m=128 MiB, t=3, p=1**; floors m≥16 MiB, t≥2, p=1;
  salt 16 B; output 32 B → IKM for HKDF. Benchmark (Q2, 2026-10-22, dev machine):
  128 MiB/t3 ≈ 172 ms median; min-spec projection 0.35–0.7 s (inside the 0.3–1.0 s band).
- **AES-256-GCM**: 96-bit random nonce (wraps + envelopes), 128-bit tag; counter-derived chunk
  nonces reserved for attachments (Phase 4, TLS-1.3-style IV⊕counter).
- **HKDF-SHA-256**: `info = "ganjineh.v1" ‖ 0x00 ‖ label`; `salt = VaultID` (16 B) in every
  derivation — every key is cryptographically vault-bound.
- **Header MAC**: HMAC-SHA-256 over header bytes 0x000–0x0FF, keyed by `HKDF(VMK, "header-mac")`.
- **Envelope (PMENV1)**: magic(6)‖version(1)‖aead_id(2 LE)‖key_domain(2 LE)‖nonce(12)‖ct‖tag(16);
  strict parse with length caps (≤64 MiB payload), typed rejections.

## Documented Deviations from the Phase 0 Design

1. **Engine parallelism = 1** (ADR-003 revision): NSec 26 / libsodium `crypto_pwhash` supports
   p=1 only, and libsodium's API has no parallelism parameter at all. Defender-side p does not
   change per-guess attacker cost; memory `m` was raised 64→128 MiB instead. The `parallelism`
   field remains in the versioned KDF metadata for future engines.
2. **Wrap AAD = canonical record, not the full header span** (vault-file-format.md §2 Revision 1):
   the Phase 0 design (AAD = header[0x000–0x0FF]) is internally contradictory — the AAD would
   include the wrap records themselves (self-reference) and would break the *other* path's
   verification whenever one wrap is rotated. Corrected model:
   - wrap AAD = `"GPWRP1"‖purpose‖VaultID‖format_version‖kdf_id‖aead_id` (27 B, canonical);
   - KDF salt+params are authenticated *transitively* for the password path (wrong values ⇒ wrong
     PEK ⇒ tag failure);
   - full-header structure+counter authenticated post-unlock by the header MAC;
   - whole-file rollback remains detectable only post-unlock via `header_counter` vs the registry
     high-water mark (documented limitation, threat-model R-2).
3. **Persistence speaks raw SQLitePCL, not Microsoft.Data.Sqlite**: MDS bundles `e_sqlite3`,
   whose battery races `bundle_e_sqlcipher` and can silently yield an unencrypted engine
   (observed in the Phase 2 spike). The provider is now set explicitly, once, per process.

## Nonce Policy

Wraps and item envelopes use 96-bit CSPRNG nonces: collision probability ≈ n²/2⁹⁷; at 10⁶
envelopes per vault ≈ 4×10⁻¹⁷ — acceptable. Attachment chunks (Phase 4) will use structurally
collision-free counter-derived nonces. No nonce reuse was observed across the 2000-case mutation
fuzz corpus or lifecycle tests (deterministic-RNG tests cover uniqueness explicitly).

## Unicode / Password Encoding

Master passwords are UTF-8 encoded exactly as typed: **no** Unicode normalization, no trimming.
The same typed bytes always produce the same key; visually-equal strings in different
normalization forms produce different keys by design (tested, including ZWNJ and emoji).
