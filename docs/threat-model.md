# Threat Model — Ganjineh Offline Password Manager

Status: **Phase 0 — Proposed** | Method: assets → adversaries → STRIDE → attack trees → residual risks

## 1. Scope and Assumptions

- Single-user Windows 10/11 desktop application; no network features in v1.
- **We assume the OS itself is not compromised** while the user works: no kernel rootkits,
  no malicious drivers, no compromised OS binaries. Attacks at that level defeat all user-mode
  software equally and are out of scope (see §8).
- We assume the user can read and follow the recovery instructions; we do not assume they
  remember anything but the master password.
- Cryptography is assumed correct if and only if implemented per cryptographic-specification.md
  and verified by security-test-plan.md; this model treats implementations as attackable.

## 2. Assets

| ID | Asset | Sensitivity | Where it lives |
|----|-------|-------------|----------------|
| A1 | Vault item secrets (passwords, notes, cards, TOTP seeds) | Critical | Inside encrypted envelopes in SQLCipher DB |
| A2 | Vault Master Key (VMK) | Critical (equals A1) | Memory during unlock; wrapped in header |
| A3 | Master Password | Critical | User's memory; transitively protects A2 |
| A4 | Recovery Key | Critical (equals A1) | User's offline storage; never persisted by app |
| A5 | Attachments | High | Chunked envelope files |
| A6 | Vault metadata (names, structure, timestamps) | Moderate | Encrypted in DB; Vault ID + file paths are non-sensitive |
| A7 | Session keyring (derived keys) | Critical while unlocked | Process memory only |
| A8 | Clipboard contents after copy | High | OS clipboard (time-boxed by app) |

## 3. Trust Boundaries

```
 [User] ──(keyboard)──> [Ganjineh process]
                          │  trusted compute base: app + .NET runtime + SQLCipher/libraries
 ═════════════════════════╪══════════════════════════════════════════════
 TB1  process boundary    │  other processes, screen capture, keyloggers (out of TCB)
 ═════════════════════════╪══════════════════════════════════════════════
 TB2  filesystem          │  vault files at rest (header.bin, vault.db, attachments/)
 TB3  removable media     │  portable root on USB (adds theft + unsafe-removal risks)
 TB4  OS services         │  clipboard, pagefile, hibernation file, crash dumps, event log
 TB5  import/export files │  third-party exports (untrusted input), plaintext exports
```

## 4. Adversaries

| ID | Adversary | Capability | Primary attack |
|----|-----------|-----------|----------------|
| ADV-1 | Opportunistic thief | Possesses powered-off PC or USB drive | Offline attack on vault at rest |
| ADV-2 | Malware with user privileges | Reads/writes files, memory, clipboard, input | Steal secrets while unlocked; corrupt/rollback vault |
| ADV-3 | Curious co-user of shared PC | Local account, no admin; can read user files | Open vault files, read registry/settings, shoulder-surf |
| ADV-4 | Forensic examiner | Disk images, old file versions (VSS, Recycle Bin) | Recover deleted secrets/old vault versions |
| ADV-5 | Brute-forcer with vault copy | Offline GPU cracking rig | Weak master password search against header |
| ADV-6 | Malicious file supplier | Crafts import files / attachments / vault headers | Parser exploits, path traversal, resource exhaustion |
| ADV-7 | Physical observer | Screen view, cameras | Revealed secrets, TOTP codes |
| ADV-8 | Supply chain | Compromised NuGet package / build toolchain | Fake crypto, backdoored binary |

## 5. STRIDE Analysis (per component)

| Component | Threat | Mitigation (design refs) |
|-----------|--------|--------------------------|
| Bootstrap header | **S**: forged header; **T**: rollback to older header; **R**: repudiation n/a; **I**: metadata leak; **D**: malformed header crash | Wrapping AEAD tags bind all header bytes 0x00–0xFF as AAD (crypto-spec §6); post-unlock HMAC + monotonic `header_counter` detects rollback (vault-format §6); strict bounded parsing + fuzzing (§7); header holds only non-sensitive fields |
| Unlock path | **S**: bypass; **I**: master password leakage via timing/log | AEAD tag failure = single generic error; no logging of credentials; constant-ish behavior regardless of failure stage |
| SQLCipher DB | **I**: read at rest | Genuine SQLCipher + raw derived key; at-rest test with unkeyed SQLite (ST-DB-1); WAL/journal are cipher-encrypted |
| Item envelopes | **S/T/I**: tamper, cross-record splicing | AES-256-GCM with AAD binding vault ID + item ID + purpose + version (crypto-spec §7); wrong-ID splices fail auth |
| Attachments | **I/T/D**: tamper, truncation, zip-bomb-like expansion | Chunked AEAD, chunk index + total size in AAD, bounded allocation; final empty chunk authenticates EOF |
| Recovery path | **S**: bypass without key; **I**: recovery key logged/shown | ≥256-bit random key; shown once with confirmation; never persisted/logged; wrap revocation on rotation (recovery-design) |
| Multi-vault | **S/E**: cross-vault unlock, key confusion | Per-vault DB, salt, keys; HKDF salt = Vault ID binds all derived keys to the vault; session keyring zeroized on switch |
| Import | **I/D**: injection, traversal, resource abuse | Treat imports as untrusted: bounded parse, no path traversal on attachment names, dry-run preview, secrets only in memory |
| Clipboard | **I**: clipboard history/cloud sync | Time-boxed clear; documented Win+V residual risk (R-8) |
| Logs/dumps | **I**: secret leakage | Redaction allowlist; dumps disabled; secrets modeled as cleared byte buffers, never in strings passed to log |

## 6. Attack Trees (key scenarios)

**AT-1: Read vault at rest (ADV-1/5)**
1. Copy `vault.db` → blocked by SQLCipher (needs DB key = HKDF(VMK,…)).
2. Read header → contains only non-sensitive fields + wrapped VMK.
3. Brute-force master password → cost = Argon2id(64 MiB, t=3) per guess + AEAD verification;
   success requires weak password. *Residual: user-chosen weak passphrase.*
4. Brute-force recovery key → 2^256, infeasible. ✓

**AT-2: Read vault while unlocked (ADV-2/7)**
1. Scrape process memory → keyring is exposed; *residual accepted* (R-1): user-mode app cannot
   fully prevent this; mitigations = zeroize on lock/switch, no persistent plaintext, secrets as
   `byte[]`/`char[]` cleared after use, pagefile risk documented.
2. Read clipboard → cleared after 60 s; *residual: OS clipboard history (R-8).*
3. Screen capture/keylog → out of TCB; UI mitigations only (blur-on-deactivate, auto re-hide).

**AT-3: Rollback the vault (ADV-2 with prior copy)**
1. Replace header+DB with older snapshot → internally consistent; unlock succeeds against the
   *old* data. Detected post-unlock via `header_counter` mismatch vs. cached value and explicit
   user warning; **cannot be fully prevented without trusted storage** → documented limitation R-2.

## 7. Explicit Limitations We Commit To Documenting (not hiding)

- Vault deletion does not guarantee erasure on SSD/COW filesystems (R-6).
- Rollback to an older full copy of a vault is undetectable before unlock and only warnable
  after (R-2).
- An attacker holding both an *old encrypted vault copy* and the Recovery Key from that era can
  open that old copy even after recovery-key rotation (R-3).
- Recovery Key holders have full vault access; rotation does not expel them from copies they
  already have (R-3).
- Memory hygiene is best-effort in a garbage-collected runtime (R-1).

## 8. Out of Scope

Kernel rootkits; DMA/cold-boot/DRAM-remanence attacks; hardware implants; side channels
(cache/timing/power); compromised OS or GPU drivers; the user voluntarily disclosing secrets;
legal/coercion; vulnerabilities in Windows itself.

## 9. Residual Risk Register

| ID | Risk | Severity | Mitigation | Status |
|----|------|----------|-----------|--------|
| R-1 | Memory scraping of unlocked session | High | Zeroize on lock/switch; no plaintext at rest; doc limits | **Accepted, documented** |
| R-2 | Vault rollback with older full copy | Medium | `header_counter` post-unlock warning | Accepted, documented |
| R-3 | Old copy + old Recovery Key still opens old copy | Medium | Docs; rotation guidance | Accepted, documented |
| R-4 | Weak user master password | High | Argon2id cost floor; strength meter; generator | Mitigated, never eliminated |
| R-5 | Supply-chain (NuGet) compromise | High | Pinned versions; ADR-002 genuineness test; release checklist | Mitigated |
| R-6 | No guaranteed secure delete on SSD | Low | Refuse to claim erasure; backup-before-delete guidance | Accepted |
| R-7 | Screen capture / keylogger | High | Auto re-hide, blur, autolock; out of TCB | Accepted |
| R-8 | Windows clipboard history (Win+V) / cloud clipboard | Medium | Time-boxed clear; docs advise disabling history; app cannot control OS | **Accepted, documented** |
| R-9 | Malformed input crashing parser | Medium | Bounded parsing + fuzz tests (ST-FZ-*) | Mitigated |
| R-10 | Portable drive theft while mounted/unlocked | High | Auto-lock on idle; docs warn on eject; not preventable | Accepted |
