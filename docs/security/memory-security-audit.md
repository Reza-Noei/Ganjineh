# Memory Security Audit — Phase 8

## What the code does

- `KeyMaterial` zeroizes via `CryptographicOperations.ZeroMemory` on Dispose
- `VaultKeyring.Dispose` zeroizes all six key containers
- KEKs are zeroized immediately after unwrap; VMK copies zeroized after derivation
- Recovery secrets zeroized by the caller after single display
- Password entry flows through `char[]` → UTF-8 `byte[]` (no normalization) → KDF
- No secrets in `string` fields, logs, exception messages, or settings (sentinel-scanned)

## Verified properties

- `KeyMaterial_Dispose_Zeroizes_Buffer` (reflection access to internal buffer)
- `VaultKeyring_Dispose_Zeroizes` (test accessor confirms null after dispose)
- Locked sessions invalidate keyrings; stale operations throw `StaleSessionException`
- At-rest sentinel scans cover header.bin, vault.db, WAL, -shm, vaults.json

## Limits (documented, not solvable)

1. GC relocation may copy buffers before zeroization
2. OS paging may write secret-bearing pages to `pagefile.sys` / `hiberfil.sys`
3. WER crash dumps capture process memory while unlocked
4. Process inspection by same-user malware reads all secrets in RAM

These are accepted residual risks (threat model R-1); the at-rest design assumes the OS is
not compromised. Software-only encryption cannot fully protect secrets against malware or a
local administrator controlling an already unlocked host.
