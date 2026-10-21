# ADR-002: SQLCipher Provider Selection and Genuineness Verification

Status: Proposed (blocked on Phase 2 spike) | Date: 2026-10-22 | Decides: contract §1, §5

## Context
The contract mandates a **genuine** SQLCipher-enabled provider and explicitly warns that a
package name containing "SQLCipher" proves nothing. On .NET the realistic options are:

1. **SQLitePCLRaw.bundle_e_sqlcipher** — open-source community packaging of SQLCipher
   (AES-256-CBC pages + HMAC-SHA-512), consumed via `Microsoft.Data.Sqlite`/raw ADO.NET.
2. **Zetetic SQLCipher for .NET** — commercial, signed, professionally maintained build.
3. ** application-level encryption over plain SQLite** (encrypt BLOBs, leave structure plain) —
   not SQLCipher; rejected by contract.

## Decision
Target **option 1** (`SQLitePCLRaw.bundle_e_sqlcipher`) as the default provider, gated by a
mandatory genuineness test suite; migrate to **option 2** without code changes if the spike or
supply-chain review fails, since access is via standard ADO.NET and raw-key pragmas in both.

**Genuineness gates (ST-DB-1..3, hard release blockers):**
- `PRAGMA cipher_version;` returns non-empty at open; empty ⇒ refuse vault open.
- A DB written by our stack **cannot be opened** by plain SQLite (unkeyed) — must error, not
  open-as-empty.
- WAL/`-shm` byte-scans contain no plaintext sentinels (ST-DB-2).

## Keying
SQLCipher's internal PBKDF2 is bypassed with a raw hex key (`PRAGMA key = "x'…'"` +
`kdf_iter = 0`); the DB key is `HKDF(VMK, "sqlcipher-key")` — Argon2id already did the expensive
work; double KDF would double unlock latency for no gain (crypto-specification.md §4).

## Alternatives Considered
- **Microsoft.Data.Sqlite + `SQLITE_HAS_CODEC` custom build** — rejected: we would own a native
  build pipeline (supply-chain surface) for no benefit over option 1.
- **SEE (SQLite Encryption Extension)** — commercial, fine, but not SQLCipher; contract names
  SQLCipher and its page-auth model; keep as fallback if both options above fail (unlikely).

## Consequences
- Community-build provenance must be pinned (exact package version + hash in release checklist;
  risk R-5).
- Phase 2 spike must verify win-x64 native asset restore on .NET 10 **before** crypto work
  lands (open question Q1).
- `cipher_memory_security = ON` costs performance; accepted for v1, measured in benchmarks.
