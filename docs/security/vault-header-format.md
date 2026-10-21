# Vault Header Format — v1 as implemented (`HeaderCodec`)

512 bytes, little-endian. This is the authoritative record; vault-file-format.md §2 remains the
Phase 0 design document — **Revision 1 changes the wrap-record authentication model** (see §4).

## Byte Layout (FORMAT_VERSION 1)

| Offset | Size | Field | Notes |
|--------|------|-------|-------|
| 0x000 | 8 | Magic `47 50 56 4C 54 00 00 00` | "GPVLT" |
| 0x008 | 2 | header_size = 512 | |
| 0x00A | 2 | format_version = 1 | >1 ⇒ `UnsupportedFormatException` |
| 0x00C | 2 | min_reader_version = 1 | >supported ⇒ refuse with guidance |
| 0x00E | 2 | flags = 0 | |
| 0x010 | 16 | vault_id | UUIDv4 |
| 0x020 | 2 | kdf_id = 0x0001 (Argon2id) | |
| 0x022 | 2 | reserved = 0 | |
| 0x024 | 4 | argon2_memory_kib | u32 |
| 0x028 | 4 | argon2_passes | u32 |
| 0x02C | 4 | argon2_parallelism | u32 (engine floor: 1) |
| 0x030 | 2 | password_salt_len = 16 | |
| 0x032 | 2 | reserved = 0 | |
| 0x034 | 16 | password_salt | |
| 0x044 | 2 | aead_id = 0x0001 (AES-256-GCM) | |
| 0x046 | 2 | hash_id = 0x0001 (SHA-256) | |
| 0x048 | 12 | password wrap nonce | |
| 0x054 | 48 | password wrap (ct 32 + tag 16) | |
| 0x084 | 12 | recovery wrap nonce | |
| 0x090 | 48 | recovery wrap (ct 32 + tag 16) | |
| 0x0C0 | 8 | created_at_unix | i64 |
| 0x0C8 | 8 | header_counter | u64, starts at 1, +1 per rewrite |
| 0x0D0 | 2 | envelope_format_version = 1 | |
| 0x0D2 | 46 | reserved = 0 | |
| 0x100 | 32 | header_mac = HMAC-SHA-256(header_mac_key, bytes 0x000–0x0FF) | |
| 0x120 | 224 | reserved = 0 | |

## Parsing Rules (strictness contract, fuzz-tested)

Exact size; exact magic; known format/reader versions; zero flags; zero reserved bytes; known
algorithm ids; salt_len = 16. All violations are typed (`CryptoFormatException` /
`UnsupportedFormatException`); 2 000-case deterministic bit-flip corpus passes with typed errors
only (ST-FZ-1). KDF floors are validated on open — below-floor parameters refuse to open
(downgrade-rollback guard).

## Authentication Model — Revision 1 (normative)

**What is authenticated BEFORE unlock:** each wrap record's AEAD tag with canonical AAD
`"GPWRP1"‖purpose‖VaultID‖format_version‖kdf_id‖aead_id`. Consequences:
- wrong password/recovery key ⇒ tag failure (generic `CryptoAuthenticationException`);
- password-path KDF salt/params are bound transitively (any edit ⇒ wrong PEK ⇒ tag failure);
- cross-vault wrap substitution ⇒ AAD VaultID mismatch ⇒ tag failure;
- the recovery wrap is NOT affected by password-wrap rotation (independent records), and vice
  versa — this is why Revision 1 replaced the Phase 0 full-span AAD, which was self-referential
  and would have invalidated the recovery wrap on every master-password change.

**What is authenticated only AFTER unlock (VMK available):** `header_mac` covers the full
0x000–0x0FF span — both wrap records, counter, timestamps, envelope version. `header_counter`
is compared to the registry high-water mark; regression ⇒ `VaultSession.RollbackWarning` (whole
older-snapshot rollback is otherwise internally consistent and CANNOT be detected pre-unlock —
documented limitation R-2; authenticated encryption alone does not prevent rollback).

## Rewrite Protocol (crash-safe)

`HeaderFileStore.WriteUpdated` = counter++ → serialize (fresh zero MAC region) → HMAC over
0x000–0x0FF → patch MAC → `AtomicFile.WriteAllBytes` (sibling temp file, flush-to-disk,
`File.Replace` with `.bak`). Fault-injection tests prove the previous header stays byte-identical
and fully usable when the process "crashes" at either fault point; stale temp files are cleaned
on the next open.
