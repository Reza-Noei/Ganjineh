# Key Hierarchy — as implemented (Phase 2)

Companion to docs/key-management.md (design) — this file records the implemented reality.

## Derivation Tree

```
Master Password ─Argon2id(m,t,p; salt)─▶ IKM ─HKDF(salt=VaultID, "password-kek")─▶ PEK ─┐
                                                                                        ├ AES-GCM unwrap ─▶ VMK
Recovery Secret ───────────HKDF(salt=VaultID, "recovery-kek")──────────────▶ RKEK ──────┘
                                                                                          │
        VMK ──HKDF(salt=VaultID, label)──▶ db_key          (SQLCipher raw key)            │
             ├──▶ item_key        (payload envelopes)                                     │
             ├──▶ attachment_key  (attachment chunks, Phase 4)                            │
             ├──▶ backup_wrap_key (.gvbak, Phase 6)                                       │
             └──▶ header_mac_key  (HMAC over header 0x000–0x0FF)                          ◀─ generated at vault creation
```

- `info` for every HKDF call = ASCII("ganjineh.v1") ‖ 0x00 ‖ ASCII(label); labels:
  `password-kek`, `recovery-kek`, `sqlcipher-key`, `item-encryption`,
  `attachment-encryption`, `backup-wrap`, `header-mac`.
- `salt` = the 16-byte VaultID in **every** derivation ⇒ keys cannot collide across vaults,
  and wrap records cannot be transplanted between vaults (also enforced by wrap AAD).

## Wrap Records

`KeyWrapRecord` = nonce(12) ‖ ciphertext(32) ‖ tag(16); AES-256-GCM under PEK or RKEK with
canonical AAD `"GPWRP1"‖purpose‖VaultID‖format_version‖kdf_id‖aead_id` (Revision 1 — see
cryptographic-implementation.md §Deviations). Both records live in the bootstrap header and
independently wrap the same VMK.

## Lifecycle (implemented)

| Key | Created | In memory | Persisted | Destroyed |
|-----|---------|-----------|-----------|-----------|
| VMK | vault create | inside `VaultKeyring` while unlocked | 2 wraps in header | lock ⇒ zeroize |
| PEK / RKEK | per unlock attempt | microseconds | never | immediately after unwrap |
| Recovery Secret | create / rotate | only for encode | **never** | caller disposes |
| Derived keys | unlock | session | never | lock ⇒ `VaultKeyring.Dispose()` |

- `VaultKeyring` and `KeyMaterial` zeroize on Dispose (`CryptographicOperations.ZeroMemory`);
  managed-runtime limits documented in secure-memory-limitations.md.
- Sessions are single-active; `VaultSession.Lock()` closes the DB (WAL checkpointed) then
  zeroizes the keyring; `Dispose` is idempotent.
- Password change: fresh salt + new password wrap; VMK/data untouched (verified by
  VaultLifecycleTests.Master_Password_Change_*).
- Recovery rotation: new wrap replaces old in one atomic header write (old key fails on the live
  vault; older *snapshots* paired with the old key remain openable — R-3).

## Verification

ST-KAT + ST-SEC tests: cross-implementation Argon2id agreement (BouncyCastle), RFC 5869 HKDF
vectors, wrap round-trip/wrong-KEK/cross-vault/purpose-binding failures, keyring separation and
vault binding, zeroization behavior, lifecycle isolation (VaultLifecycleTests,
AtRestAndTamperTests) — see cryptographic-test-results.md.
