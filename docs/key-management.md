# Key Management — Ganjineh

Status: **Phase 0 — Proposed** | Companion to cryptographic-specification.md (derivation details there).

## 1. Hierarchy

```
Master Password ──Argon2id──▶ PEK ─┐          (password path)
                                   ├─AES-GCM unwrap─▶ VMK ─┬─HKDF─▶ db_key          (SQLCipher)
Recovery Key ────────HKDF─────▶ RKEK┘                     ├─HKDF─▶ item_key        (payload envelopes)
                                                          ├─HKDF─▶ attach_key      (attachment chunks)
             RecoverySecret ──(encoded to user)           ├─HKDF─▶ backup_wrap_key (.gvbak)
                                                          └─HKDF─▶ header_mac_key  (header HMAC)
```

- The **VMK is random** and independent of the master password. Password changes never touch
  data; they only create a new wrap record. All data keys descend from VMK via HKDF.
- Unlocking one vault loads only that vault's keys. There is no shared key material of any kind
  between vaults; HKDF salt = VaultID makes cross-vault key reuse cryptographically impossible.

## 2. Key Inventory and Lifecycle

| Key | Created | In memory | Persisted as | Destroyed |
|-----|---------|-----------|--------------|-----------|
| VMK | vault create | after first successful unwrap | 2 wraps (password + recovery) in header | vault delete / app lock |
| PEK | each unlock (transient) | milliseconds during unlock | never | immediately after unwrap |
| RKEK | each recovery unlock (transient) | milliseconds during recovery | never | immediately after unwrap |
| RecoverySecret | create / rotate | only during setup/rotation display | **never** (user stores it offline) | on window close |
| Derived keys (db/item/attach/backup/header-mac) | at unlock | for session duration | never | zeroized at lock |
| file_key (per attachment) | attachment create | on open/save | sealed envelope in DB | with attachment delete |

## 3. Session Keyring

- One `VaultKeyring` instance per unlocked vault, owned by the vault session; an explicit
  `IDisposable` whose `Dispose` is the only path to its buffers.
- Secret material is held in `byte[]`; copying only via explicit clone; **no `string`s** for keys,
  passwords (outside the transient password-entry control), or derived secrets.
- Lock / vault switch / app exit ⇒ ordered teardown: close DB connection → zeroize all keyring
  buffers (`CryptographicOperations.ZeroMemory`) → drop envelopes cache → clear UI-bound secret
  state (ui-ux-specification.md §9).
- Lock is also triggered by idle timeout and Windows session lock (`SystemEvents.SessionSwitch`).

## 4. Memory Hygiene — Commitments and Honest Limits

Commitments: zeroize on lock/switch/dispose; pin and zero large secret buffers where practical;
no secrets in exception messages, logs, dumps (dumps disabled by default), WPF binding state
beyond the transient reveal control, or `AppDomain`-wide caches.

Limits (documented in-product): a garbage-collected runtime cannot guarantee the absence of
secret copies (GC may relocate buffers before zeroization; compiler/runtime may transiently
duplicate). This is residual risk R-1 in threat-model.md — accepted, because the alternative
(writing a non-GC crypto core) is out of scope for v1 and the at-rest protections are unaffected.

## 5. Password Unlock Path (runtime view)

1. Read header; validate magic/version; parse with strict bounds (no allocation from untrusted lengths).
2. `ikm = Argon2id(MP, salt, params)`; `pek = HKDF(ikm, salt=VaultID, info="password-kek")`.
3. `vmk = AesGcm.Open(pek, wrap_nonce, wrap_ct, aad=HEADER_AAD)`.
   - Tag failure ⇒ `AuthenticationFailed` (wrong password or tampering — deliberately indistinguishable).
4. `mac = HMAC-SHA256(HKDF(vmk,…,"header-mac"), header[0x000–0x100])`; mismatch ⇒ rollback/tamper warning (R-2).
5. Check `header_counter` against the high-water mark cached in the vault registry; regress ⇒ explicit user warning.
6. Open SQLCipher with `db_key`; run `cipher_version` + `integrity_check`.
7. Populate keyring; unlock complete.

## 6. Rotation Workflows

**Change master password** (FR-VL-7): derive new PEK from (new MP, *same* salt + params or fresh
salt — fresh salt, new wrap) → write *new* password wrap record → rewrite header atomically
(temp file + flush + replace) → old wrap record removed. VMK unchanged; no data re-encryption.
Optional **deep rotation** (post-compromise hygiene): new VMK → re-wrap → re-encrypt all payloads,
attachments and DB under new keys on a temp copy → atomic swap. Offered as "Full re-encryption"
with progress UI (Phase 3 for basic, Phase 6 for deep).

**Rotate recovery key** (FR-RC-4): new 32-B RecoverySecret → new RKEK wrap replaces the old
recovery wrap record (old wrap deleted in the same atomic header write) → RecoverySecret shown
once with save-confirmation (recovery-design.md §4). Prior data untouched. Caveat displayed:
older *vault copies* paired with the old Recovery Key remain openable by whoever holds both (R-3).

**KDF parameter change** (e.g. after hardware upgrade): only affects future unlocks; applied by
re-running the password path and writing a new wrap + params in one header transaction. No data
re-encryption; recovery wrap re-derived from stored-in-memory VMK directly (no RecoverySecret needed).

## 7. Key Isolation Guarantees (test obligations)

| Guarantee | Test |
|-----------|------|
| Unlocked vault A grants no access to vault B | ST-INT-1 (vault isolation) |
| Keys never cross vaults even with identical passwords | ST-INT-2 (same MP, two vaults, distinct wraps+DB bytes) |
| Lock zeroizes keyring (observable via test accessor) | ST-SEC-5 |
| Wrong-vault splices of envelopes fail authentication | ST-SEC-3 (wrong Vault ID binding) |
