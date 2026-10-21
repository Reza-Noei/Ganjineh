# ADR-003: Argon2id as Password KDF

Status: Proposed (parameters pending hardware benchmark) | Date: 2026-10-22 | Decides: contract §3

## Context
The master password is the primary human-memorizable secret protecting the VMK. Offline GPU
brute force (ADV-5) is the dominant at-rest attack; the KDF must make each guess expensive in
memory as well as time. .NET has no built-in Argon2.

## Decision
**Argon2id** (side-channel-resistant hybrid), parameters per vault, stored in the authenticated
header: baseline **m=64 MiB, t=3, p=4**; hard floors m≥16 MiB, t≥2, p≥1; target unlock band
0.3–1.0 s; benchmark-and-record procedure in crypto-specification.md §3.1. Library: libsodium
via **NSec** (audited binding; native win-x64 packaging verified in Phase 2 spike).

Password-key binding: Argon2 output feeds HKDF (salt = VaultID) to produce PEK, so the KDF result
is vault-specific and the wrap AEAD (AAD covering the parameters) makes parameter tampering or
downgrade fail authentication rather than succeed cheaply (ST-SEC-6).

## Alternatives Considered
- **PBKDF2-HMAC-SHA256** — SQLCipher's default; GPU/ASIC-friendly; rejected except inside
  SQLCipher itself (which we bypass with raw keys).
- **scrypt** — sound, but Argon2id is the PHC winner with better cache-timing resistance; no
  first-class libsodium perf advantage either way.
- **Balloon hashing** — promising but less battle-tested in libraries; revisit only if a future
  format version migrates.

## Consequences
- Unlock latency is *by design* ~1 s; UX must frame it as a security feature (ui-ux-spec §4 screen 2).
- Per-vault parameters enable per-vault tuning; header authentication prevents silent downgrade.
- Floor-violating headers open only with explicit user confirmation (rollback case R-2).
- Memory-hard KDF runs on the UI-adjacent path → must execute off the dispatcher thread.
