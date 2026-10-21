# ADR-005: Offline Recovery Key Mechanism

Status: Proposed | Date: 2026-10-22 | Decides: contract §4

## Context
A forgotten master password must not mean data loss, with no server available. Any recovery
mechanism must be independent of the master password, resistant to brute force even with the
vault file in hand, and honest about what revocation can and cannot do.

## Decision
**Independent high-entropy Recovery Key** — 256-bit CSPRNG secret, Crockford-Base32 encoded with
version prefix + CRC-32 checksum (`PMRK1-…`, recovery-design.md §2). It derives RKEK via HKDF
(full entropy ⇒ no memory-hard KDF needed, unlock is instant) and independently wraps the same
VMK in its own authenticated header record. Workflows: setup with mandatory transcription proof
(4-group challenge), unlock, master-password reset, rotation with atomic wrap revocation.

Explicitly rejected: security questions, stored hints, email/SMS recovery, "partial" recovery —
each either weakens the root of trust or fakes capability (contract: no fake recovery).

## Security Properties
- Brute-forcing the recovery path is 2^256 — harder than the password path by construction;
  users get a *stronger* root than their password, not a weaker back door.
- Revocation is forward-only: rotation deletes the old wrap from the live vault but cannot reach
  older vault copies; old copy + old key remains openable (threat-model R-3) — surfaced in UI
  and docs, never implied otherwise.
- Plaintext Recovery Key is never persisted, logged, or included in any export; UI shows it only
  inside explicit setup/rotation flows with save-confirmation (ST-SEC-9).

## Alternatives Considered
- **Shamir split (2-of-3 shares)** — better for teams; operational overkill and confusion risk
  for single-user v1; format is versioned so a future variant can add it without breaking v1.
- **Recovery file (keyfile) instead of typed key** — supports the same crypto, but users store
  keyfiles next to the vault; typed+printed key with checksum has better separation properties.
  (Import of a pasted key from a file is supported; the *format* is the same.)
- **Deterministic recovery from master password** ("you can always derive it") — defeats
  forgotten-password scenarios and couples both roots; rejected.

## Consequences
- Setup UX is longer (mandatory challenge) — accepted; recovery failure at restore time is the
  worst product outcome possible.
- Documentation and tooltips must repeatedly state: holder of the key = holder of the vault.
