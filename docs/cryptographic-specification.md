# Cryptographic Specification — Ganjineh

Status: **Phase 0 — Proposed** | Normative for Phase 2 implementation. All offsets little-endian.
No primitive outside this document may be introduced without a new ADR.

## 1. Primitives and Libraries

| Purpose | Primitive | Library (planned) | Note |
|---------|-----------|-------------------|------|
| Password KDF | Argon2id | libsodium `crypto_pwhash` via **NSec** | .NET has no built-in Argon2; NSec = audited libsodium wrapper (verify win-x64 native packaging — Q1) |
| Key derivation | HKDF-SHA-256 (RFC 5869) | .NET `System.Security.Cryptography.HKDF` | Built-in from .NET 5+ |
| AEAD | AES-256-GCM | .NET `AesGcm` | Hardware-accelerated; tag size fixed at 16 bytes |
| CSPRNG | 256-bit random | .NET `RandomNumberGenerator` | Never `System.Random` for secrets |
| DB encryption | SQLCipher 4 (AES-256-CBC + HMAC-SHA-512 pages) | SQLCipher provider (ADR-002) | Keyed with raw derived key, see §4 |
| TOTP | HMAC-based RFC 6238 | In-house over `HMACSHA1/256/512` | Known-answer tests mandatory |

**Forbidden**: home-rolled ciphers/hashes/MACs/KDFs; ECB; unauthenticated encryption; `DES/3DES/RC4/MD5/SHA-1` (SHA-1 allowed *only* inside TOTP for service compatibility); `Guid.NewGuid`/ticks as randomness; `SecureString` for new secret storage; secrets in `string` fields beyond transient UI display.

## 2. Identifiers

| Identifier | Value | Meaning |
|------------|-------|---------|
| `KDF_ID` | 0x0001 | Argon2id |
| `AEAD_ID` | 0x0001 | AES-256-GCM, 96-bit nonce, 128-bit tag |
| `HASH_ID` | 0x0001 | SHA-256 (HKDF, HMAC) |
| `FORMAT_VERSION` | 1 | Vault header format (vault-file-format.md) |
| `ENVELOPE_VERSION` | 1 | Item/attachment/backup envelope format |
| Domain prefix | `"ganjineh.v1"` | First byte string of every HKDF `info` |

## 3. Keys and Derivation

All keys are 256-bit. `VaultID` = 128-bit UUIDv4 (16 bytes, big-endian encoding of the UUID).

| Key | Size | Derivation | Purpose |
|-----|------|-----------|---------|
| **VMK** — Vault Master Key | 32 B | `RandomNumberGenerator` at vault creation | Root of vault hierarchy; never used directly on data |
| **PEK** — Password Key Encryption Key | 32 B | `HKDF(ikm=Argon2id(MP, salt, params), salt=VaultID, info="ganjineh.v1‖0x00‖"password-kek")` | Unwraps VMK (password path) |
| **RKEK** — Recovery Key Encryption Key | 32 B | `HKDF(ikm=RecoverySecret, salt=VaultID, info="…‖"recovery-kek")` | Unwraps VMK (recovery path). HKDF is sufficient: input already has 256-bit entropy, no password-guessing surface |
| `db_key` | 32 B | `HKDF(ikm=VMK, salt=VaultID, info="…‖"sqlcipher-key")` | SQLCipher raw key |
| `item_key` | 32 B | `HKDF(ikm=VMK, salt=VaultID, info="…‖"item-encryption")` | AEAD key for item payload envelopes |
| `attach_key` | 32 B | `HKDF(ikm=VMK, salt=VaultID, info="…‖"attachment-encryption")` | AEAD key for attachment chunk envelopes |
| `backup_wrap_key` | 32 B | `HKDF(ikm=VMK, salt=VaultID, info="…‖"backup-wrap")` | Wraps backup file keys inside `.gvbak` |
| `header_mac_key` | 32 B | `HKDF(ikm=VMK, salt=VaultID, info="…‖"header-mac")` | HMAC over header bytes (post-unlock integrity + anti-rollback) |
| `RecoverySecret` | 32 B | CSPRNG at vault creation / rotation | Encoded as the user-visible Recovery Key |

HKDF `info` encoding (normative): `ASCII("ganjineh.v1") ‖ 0x00 ‖ ASCII("<label>")`.
`salt` = the 16-byte VaultID in every derivation — this binds every key to its vault and makes
key reuse across vaults cryptographically detectable rather than merely forbidden.

### 3.1 Argon2id Parameters — revised as built (ADR-003 revision, see docs/security/cryptographic-implementation.md)

| Parameter | Shipped baseline | Floor (hard min) | Notes |
|-----------|------------------|------------------|-------|
| Memory `m` | **128 MiB** | 16 MiB | Dominant cost dimension |
| Iterations `t` | 3 | 2 | |
| Parallelism `p` | **1 (engine limit)** | 1 | NSec 26/libsodium supports p=1 only; field kept in metadata for future engines |
| Salt | 16 B CSPRNG | 16 B | Per vault, stored in header |
| Output | 32 B | | IKM for HKDF |

Benchmark (Q2 closed, 2026-10-22, dev machine): 128 MiB/t3/p1 ≈ **172 ms median** (p95 183 ms);
projected 0.35–0.7 s on min-spec hardware — inside the 0.3–1.0 s unlock band. Measured points:
32 MiB/t3 39 ms; 64 MiB/t3 85 ms; 64 MiB/t2 61 ms. Procedure unchanged; parameters are
per-vault and authenticated (§6) — tampering yields a wrong PEK and fails unwrap, so an attacker
cannot downgrade cost. Below-floor headers refuse to open (downgrade-rollback guard).

## 4. SQLCipher Keying (double-KDF pitfall)

SQLCipher would normally run its own PBKDF2 over a passphrase. We bypass it by supplying a raw
hex key — Argon2id already did the expensive work:

```
PRAGMA key = "x'<64 hex chars of db_key>'";
PRAGMA cipher_page_size = 4096;
PRAGMA kdf_iter = 0;               -- must accompany raw key usage
PRAGMA cipher_memory_security = ON;
PRAGMA journal_mode = WAL;         -- WAL/-shm content is also cipher-encrypted
PRAGMA synchronous = FULL;
PRAGMA temp_store = MEMORY;
```

Verification (`PRAGMA cipher_version;`) returning non-empty is a startup precondition; empty
result ⇒ refuse to open the vault (this is the "package name ≠ encryption" guard, ADR-002).

## 5. AEAD Conventions (all AES-256-GCM)

- Nonce: 96-bit. Wrapping envelopes and item envelopes: **random** 96-bit from CSPRNG.
  Collision bound: p ≈ n²/2⁹⁷; at 10⁶ envelopes per vault, p ≈ 4×10⁻¹⁷ — acceptable and
  documented. Attachment chunk nonces are **counter-derived** (§8) to make collisions structurally impossible.
- Tag: 128-bit, appended after ciphertext.
- On tag failure: single generic error (`AuthenticationFailed`), no stage disclosure, no retry hints.

## 6. VMK Wrapping (header records)

Two independent wrap records live in the bootstrap header (byte layout in vault-file-format.md):

```
wrap_nonce      = 12 B random
wrap_ct         = AES256GCM(key=PEK|RKEK, nonce, plaintext=VMK(32B), aad=HEADER_AAD)
HEADER_AAD      = header bytes 0x000–0x0FF   (everything before the header MAC)
```

Because `HEADER_AAD` covers the KDF parameters, salts, format IDs and both wrap records, **any**
header mutation breaks pre-unlock authentication. Replacing the *entire* header with an older
internally-consistent copy is the rollback case → post-unlock HMAC check + `header_counter`
(vault-file-format.md §6, threat R-2).

## 7. Item Payload Envelope (versioned format, `ENVELOPE_VERSION = 1`)

One envelope per item version. Plaintext = canonical JSON (UTF-8) of the item payload
(all sensitive fields; DB stores only structural columns — database-design.md §3).

```
offset  size  field
0x00    6     magic  "PMENV1"                    (0x50 4D 45 4E 56 31)
0x06    1     envelope_version           = 1
0x07    2     aead_id                    = 0x0001
0x09    2     key_domain (1=item, 2=attachment, 3=backup-inner, 4=meta)
0x0B    12    nonce
0x17    N     ciphertext (N = plaintext length)
0x17+N  16    tag
```

AAD (normative, encoded once and hashed into the AEAD):

```
AAD = "PMENV1"‖version‖aead_id‖key_domain‖nonce_len(2B)‖nonce
      ‖ vault_id(16)
      ‖ record_id   (item UUID / attachment UUID / "header" / backup ID)
      ‖ purpose     (e.g. "payload.v1", "chunk:<u64 index>", "wrap:password", "wrap:recovery")
```

Wrong record splicing (moving ciphertext between items/vaults) changes `record_id`/`vault_id`
in AAD ⇒ tag failure. `key_domain` mismatch (e.g. attachment key on item data) ⇒ tag failure.

## 8. Attachment Chunked Envelope

Per attachment: `file_key` (32 B CSPRNG) + `chunk_iv` (12 B CSPRNG). `file_key` is itself sealed
in an envelope (key_domain=attachment, purpose `"filekey"`) and stored beside metadata.
Payload is split into **4 MiB chunks**:

```
nonce_i  = chunk_iv XOR u64(i) placed in the low 8 bytes   (TLS 1.3-style construction)
chunk_i  = AES256GCM(key=attach_key, nonce_i, plaintext=chunk_i,
                     aad = standard AAD with record_id=attachment UUID,
                           purpose="chunk:<i>", extra = total_file_size(8B))
terminator = zero-length chunk (index = last+1)  → authenticates completeness (anti-truncation)
```

File on disk = `PMENV1`-style chunk header (12 B `chunk_iv`, 8 B total size) followed by
`[len(4B)‖ct‖tag]` per chunk. Sequential streaming decrypt with bounded buffering (max 2 chunks
in memory). Any chunk/tag/truncation failure aborts the whole attachment.

## 9. Backup Envelope (`.gvbak`)

`backup_key` (32 B CSPRNG, per backup) encrypts a snapshot; `backup_wrap_key` seals `backup_key`
in the same file. Manifest (vault ID, schema version, created-at, inner SHA-256, counts) is part
of AAD so partial splicing of old backups is detected. Details: database-design.md §7.

## 10. Randomness

All secret randomness via `RandomNumberGenerator.Fill` / `GetBytes`. CSPRNG is never reseeded by
app code; no entropy estimation fallbacks; generators are never reused across vaults.

## 11. Failure Handling Rules

1. Malformed header/envelope/chunk (bad magic, version, oversized lengths) ⇒ typed parse error,
   **no dynamic allocation proportional to untrusted values** (all length caps in
   vault-file-format.md §5), no exception text containing input bytes.
2. Authentication failures are indistinguishable to logs and UI (`AuthenticationFailed`).
3. Crypto errors never include key material, salts, nonces, or plaintext fragments.
4. Every envelope parse is covered by the fuzz suite (security-test-plan.md ST-FZ-*).

## 12. Known-Answer Test Obligations

| Area | Vectors |
|------|---------|
| HKDF-SHA-256 | RFC 5869 A.1–A.3 |
| Argon2id | RFC 9106 §5.3 reference vectors (via NSec binding test) |
| AES-256-GCM | NIST CAVS (encrypt + decrypt, incl. modified-tag negative) |
| TOTP | RFC 6238 B (SHA-1/256/512, 8-digit), plus 6-digit parity |
| Base32 | RFC 4648 §10 |
| Envelope round-trip | Golden envelopes pinned in repo (regression against format drift) |

No cryptographic code ships until every KAT passes; no mocked/fake crypto in production code.
