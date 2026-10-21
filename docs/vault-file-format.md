# Vault File Format — Ganjineh v1

Status: **Phase 0 — Proposed** | Normative. Little-endian throughout.

## 1. On-Disk Layout

A vault is a **directory**, named `<VaultId>.pvault` (VaultId in canonical UUID hyphenated form),
so the whole vault relocates as one unit (portable mode requirement):

```
<8-4-4-4-12 uuid>.pvault/
├── header.bin        bootstrap header (512 B, this document)
├── vault.db          SQLCipher database (database-design.md)
└── attachments/      <attachment-uuid>.pmblob  (chunked envelopes, crypto-spec §8)
```

The vault registry (`vaults.json`) is *application state*, not part of the format, and contains
only non-sensitive fields: `vaultId`, absolute `path`, `lastOpenedAt`, optional user-entered
`localLabel` (explicitly marked "stored in plain text, this device only"). **No names, no
metadata, no counters of sensitive content** — the plaintext shared-index prohibition in the
contract is satisfied by keeping display names inside the encrypted DB; pre-unlock lists show
the local label or the file name, never a decrypted name.

## 2. Bootstrap Header — Byte Layout (FORMAT_VERSION 1, total 512 bytes)

| Offset | Size | Field | Notes |
|--------|------|-------|-------|
| 0x000 | 8 | Magic `47 50 56 4C 54 00 00 00` ("GPVLT") | |
| 0x008 | 2 | `header_size` = 512 | u16 |
| 0x00A | 2 | `format_version` = 1 | u16 |
| 0x00C | 2 | `min_reader_version` = 1 | u16; refuse if app older |
| 0x00E | 2 | `flags` = 0 | u16 |
| 0x010 | 16 | `vault_id` | UUIDv4 |
| 0x020 | 2 | `kdf_id` = 0x0001 (Argon2id) | |
| 0x022 | 2 | reserved = 0 | |
| 0x024 | 4 | `argon2_memory_kib` | u32 |
| 0x028 | 4 | `argon2_iterations` | u32 |
| 0x02C | 4 | `argon2_parallelism` | u32 |
| 0x030 | 2 | `password_salt_len` = 16 | u16 |
| 0x032 | 2 | reserved = 0 | |
| 0x034 | 16 | `password_salt` | |
| 0x044 | 2 | `aead_id` = 0x0001 (AES-256-GCM) | |
| 0x046 | 2 | `hash_id` = 0x0001 (SHA-256) | |
| 0x048 | 12 | `pw_wrap_nonce` | |
| 0x054 | 48 | `pw_wrap` = VMK ct(32) + tag(16) | AAD = bytes 0x000–0x0FF |
| 0x084 | 12 | `rec_wrap_nonce` | |
| 0x090 | 48 | `rec_wrap` = VMK ct(32) + tag(16) | AAD = bytes 0x000–0x0FF |
| 0x0C0 | 8 | `created_at_unix` | u64, seconds |
| 0x0C8 | 8 | `header_counter` | u64, starts at 1, +1 per header rewrite |
| 0x0D0 | 2 | `envelope_format_version` = 1 | |
| 0x0D2 | 46 | reserved, all zero | 0x0D2–0x0FF |
| 0x100 | 32 | `header_mac` | HMAC-SHA-256(`header_mac_key`, bytes 0x000–0x0FF) |
| 0x120 | 224 | zero padding | reserved |

Header size is fixed for v1; future versions must keep `header_size` first-class and parse only
declared fields (forward compatibility).

**Authentication story (two layers):**
1. *Pre-unlock:* both wrap records use `AAD = header[0x000–0x0FF]`, so any tampering with KDF
   params, salts, IDs or the other wrap record fails the AEAD tag before VMK release.
2. *Post-unlock:* `header_mac` (VMK-derived key) authenticates the same span and, with
   `header_counter` checked against the registry high-water mark, detects whole-header rollback
   (the one substitution layer 1 cannot see). Registry stores `lastHeaderCounter` per vaultId.

Header rewrites (MP change, recovery rotation, KDF change, counter bump) are atomic:
write `header.bin.tmp` → `FileStream.Flush(flushToDisk: true)` → `File.Replace` → fsync directory
via open/flush of parent (best effort on Windows).

## 3. What Deliberately Is *Not* in the Header

Vault display name, item counts, timestamps of use, notes, folder structure — all live encrypted
inside `vault.db`. The header exposes only what unlock itself requires; nothing in it is
commercially or personally sensitive.

## 4. Vault Registry (application state)

```jsonc
// %APPDATA%\Ganjineh\vaults.json  (installer)  |  <portable root>\Data\vaults.json
{
  "version": 1,
  "vaults": [
    { "vaultId": "uuid", "path": "C:\\...\\<uuid>.pvault",
      "lastOpenedAt": "2026-10-22T10:00:00Z",
      "lastHeaderCounter": 7,
      "localLabel": "کار",        // optional, plain text, this-device-only
      "portable": false }
  ]
}
```

Written atomically; tolerant of manual edits (missing vault = removed from list, files untouched).

## 5. Parsing Rules (hardening contract)

1. Reject: size ≠ 512; bad magic; unknown `format_version` > supported; `min_reader_version` >
   app version; unknown `kdf_id`/`aead_id`/`hash_id`; `reserved ≠ 0`; `salt_len ≠ 16`.
2. All future length fields get hard caps; **no allocation sized from the file** beyond fixed
   v1 sizes; parse into a fixed struct.
3. KDF params below floor (crypto-spec §3.1): allow open only with explicit user confirmation
   (possible parameter-downgrade rollback, R-2).
4. Any parse failure ⇒ typed error, generic user message, no input bytes in logs.
5. Parser is the primary fuzz target (ST-FZ-1/2).

## 6. Versioning and Migration Policy

- `format_version` bumps are **forward-only**. Opening a newer format with an older app is
  refused with a clear message. Upgrades migrate a *copy* first, then swap atomically; a failed
  migration leaves the original untouched (test ST-INT-9).
- Algorithm upgrades (e.g. future KDF/AEAD) add new IDs; old IDs remain readable for migration
  but are flagged in Security Center as "vault uses legacy settings".
- v1 ships with exactly one format; the migration machinery is exercised by tests even before
  v2 exists (synthetic v2 fixtures) so the path is not dead code at first use.

## 7. Attachments and Backup Files (format pointers)

- Attachment blobs: `<uuid>.pmblob` per crypto-spec §8 (self-describing chunked envelope).
- Backups: single-file `.gvbak` (database-design.md §7); restore creates a fresh
  `<new uuid>.pvault` directory — never in-place.
