# Attachment Encryption — as built (Phase 4)

Implements cryptographic-specification.md §8 exactly; this file records the concrete wire format.

## Blob layout (`<vault>/<id>.pmblob`)

```
[0x00] magic "GPBLB1\0\0"            (8 B)
[0x08] chunk_iv                      (12 B, CSPRNG per attachment)
[0x14] total_size                    (i64 LE)
[0x1C] chunk 0: [len u32 LE][ciphertext][tag 16]
[...]  chunk i: same shape
[last] terminator: len = 0, tag over empty plaintext  ← authenticates completeness
```

- Chunk size: 4 MiB plaintext (last chunk partial).
- Nonce for chunk i: `chunk_iv XOR u64(i)` (TLS 1.3-style — structurally collision-free).
- Key: `attach_key` = `HKDF(VMK, salt=VaultID, "attachment-encryption")`.
- AAD = `"GANCHK1" ‖ vault_id ‖ attachment_id ‖ chunk_index(u64 LE) ‖ total_size(i64 LE)` —
  binds chunk identity, ordering, the declared total length and vault ownership. Reordering,
  renumbering, truncation and cross-vault transplant all fail authentication (tested).
- The final zero-length chunk authenticates EOF: truncation and appended-garbage fail.

## file key

Per attachment `file_key` (32 B CSPRNG) is sealed with `attach_key` (domain=Attachment,
AAD = VaultID‖"att-filekey:<id>") and stored in the `attachments.filekey_cipher` column, nonce
prefixed. Filenames are sealed separately (`EncryptMeta`, record "att-name:<id>").

## Failure-safe ordering

1. Stream-encrypt to `<id>.pmblob.part` (temp sibling), fsync.
2. `File.Move(part → final)` (atomic on the same volume).
3. Only then insert the metadata row.
Crash at any point leaves an orphan `.part`/`.pmblob` with no row — cleaned by
`CleanupOrphans` on vault open. Metadata never references a missing blob; a blob never waits
for a row that may not come.

## Export

Streaming decrypt to the user-chosen destination; any chunk/tag/terminator failure deletes the
partial output and raises the generic authentication error. Exported filenames are sanitized
(`Path.GetInvalidFileNameChars` → `_`). Attachments are never executed automatically and are
never written to predictable plaintext temp paths.

## Limits

100 MB per attachment (validated up-front); memory use bounded at ~2 chunks regardless of size.
