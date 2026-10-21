# ADR-001: Multi-Vault Isolation Model

Status: Proposed | Date: 2026-10-22 | Decides: contract §2

## Context
The product is single-user but must support multiple fully independent vaults, each with its own
master password, recovery key, KDF parameters, and encryption hierarchy. Unlocking one vault must
grant nothing toward another, and vault switching must clear all sensitive state.

## Decision
- **One vault = one directory** (`<VaultId>.pvault/`) with its own SQLCipher database, bootstrap
  header, and attachment store (vault-file-format.md §1).
- Every key is bound to its vault cryptographically, not just procedurally: HKDF salt = VaultID
  for all derivations; envelope AAD includes VaultID. Cross-vault key or ciphertext reuse fails
  authentication by construction (crypto-specification.md §3, §7).
- One active `VaultSession` at a time; lock/switch tears down keyring (zeroize), DB connection,
  and in-memory index (key-management.md §3).
- No shared sensitive index: the registry stores VaultID, path, timestamps, optional plain-text
  local label only (vault-file-format.md §4).

## Alternatives Considered
- **Single DB with vault column + row-level keys** — rejected: one DB file becomes a shared
  plaintext-capable index of vault existence/structure; backup/restore/rollback coupling across
  vaults; larger blast radius for corruption.
- **Multiple DBs without cryptographic vault binding** (salts/keys unbound to VaultID) —
  rejected: header swaps between vaults would go undetected.

## Consequences
- Moving/backing-up/deleting a vault is a directory operation — simple and portable.
- Unlocking a second vault requires the first to lock (deliberate UX constraint, disclosed).
- Test obligations: ST-INT-1/2.
