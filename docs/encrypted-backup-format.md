# Encrypted Backup Format — `.gvbak` v1

## Layout

```
[0x000] magic "GPBAK1\0"                    (8 B, plaintext-visible)
[0x008] format_version u16 = 1              (plaintext-visible)
[0x00A] backup_id                           (16 B UUID, plaintext-visible)
[0x01A] vault_id                            (16 B UUID, plaintext-visible)
[0x02A] created_at_unix                     (i64 LE, plaintext-visible)
[0x032] header_len u32 = 512
[0x036] header copy (512 B)                 — source vault's authenticated header:
                                              KDF id/params, salt, password wrap,
                                              recovery wrap, header MAC
[0x136] meta_len u32
[0x13A] meta_nonce (12) + meta_ct + tag(16) — AES-GCM(backup_wrap_key) over manifest JSON;
                                              AAD = "GBAKM1" ‖ ALL preceding file bytes
[......] pkg_len u64
[......] GPBLB1 chunked package (ChunkedAead, 4 MiB chunks, chunk_iv + total = containerSize):
                                              plaintext = GPKG container:
                                                "GPKG1\0" + count + entries
                                                ("vault.db" snapshot, attachments/<id>.pmblob…)
                                              every chunk AAD binds vault_id ‖ backup_id ‖
                                              chunk_index ‖ container_size
[last]  file ends exactly at the terminator
```

## Plaintext-visible before decryption

Magic, format version, backup/vault IDs, creation timestamp, header length — and the header
copy's non-secret fields (KDF params, wrapped keys). Everything else (manifest contents,
database, attachments) is encrypted+authenticated.

## What is authenticated, and how

| Region | Auth | Key |
|--------|------|-----|
| All preceding file bytes | meta AEAD AAD | backup_wrap_key |
| Manifest JSON | meta AEAD ct+tag | backup_wrap_key |
| Package (DB + attachments) | per-chunk AEAD (AAD: vault‖backup‖index‖containerSize) | backup_wrap_key |
| Header copy | original header MAC + wrap AEADs (carried from source) | header_mac_key / PEK/RKEK |

`backup_wrap_key = HKDF(VMK, salt=VaultID, "backup-wrap")` — recovered from the embedded wraps
by unlocking with the Master Password or Recovery Key. Backups are NOT DPAPI-bound and are
portable across machines.

## Versioning and rejection

Unknown format versions and unsupported headerLen/metaLen/packageLen values are rejected with
typed errors (`CryptoFormatException` / `UnsupportedFormatException`) before any key material
is used.
