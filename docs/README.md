# Ganjineh (گنجینه) — Design Documentation (Phase 0)

Offline, Persian-first, multi-vault password manager for Windows. WPF / .NET 10 / SQLCipher.
**Status: Phase 0 complete, awaiting design approval before any security-sensitive code.**

## Documents

| Document | What it decides |
|----------|-----------------|
| [product-requirements.md](product-requirements.md) | Scope, FR/NFR with stable IDs, release criteria, open questions |
| [threat-model.md](threat-model.md) | Assets, adversaries, STRIDE, attack trees, residual-risk register R-1..R-10 |
| [cryptographic-specification.md](cryptographic-specification.md) | Primitives, KDF params, HKDF domains, envelope/nonce/AAD formats, KATs |
| [key-management.md](key-management.md) | Key hierarchy, session keyring, rotation workflows, memory hygiene |
| [recovery-design.md](recovery-design.md) | Recovery Key format, workflows, failure handling, honest limits |
| [vault-file-format.md](vault-file-format.md) | Vault directory + 512-byte bootstrap header byte table, parsing rules |
| [database-design.md](database-design.md) | SQLCipher config, schema, search, migrations, `.gvbak` backup format |
| [architecture.md](architecture.md) | Projects, dependency rules, service contracts, secrets handling |
| [ui-ux-specification.md](ui-ux-specification.md) | RTL design system, three-column shell, 13 screens, clipboard rules |
| [implementation-roadmap.md](implementation-roadmap.md) | Phases 0–8, exit criteria, traceability |
| [security-test-plan.md](security-test-plan.md) | ST-* test IDs: KATs, adversarial, at-rest, E2E, fuzz, benchmarks |

## Architecture Decision Records

| ADR | Decision |
|-----|----------|
| [ADR-001](adr/ADR-001-multi-vault-isolation.md) | Multi-vault isolation: one directory + one DB per vault, cryptographically bound |
| [ADR-002](adr/ADR-002-sqlcipher-selection.md) | SQLCipher provider choice + mandatory genuineness gates |
| [ADR-003](adr/ADR-003-argon2id-selection.md) | Argon2id baseline 64 MiB / t=3 / p=4, floors, benchmark procedure |
| [ADR-004](adr/ADR-004-aes-256-gcm-selection.md) | AES-256-GCM everywhere, nonce policy, AAD binding |
| [ADR-005](adr/ADR-005-recovery-key-mechanism.md) | 256-bit offline Recovery Key, forward-only revocation |
| [ADR-006](adr/ADR-006-installer-portable-compatibility.md) | One format, two distributions, no DPAPI, no machine binding |
| [ADR-007](adr/ADR-007-browser-extension-deferral.md) | Extension/native-messaging deferred; seams reserved, nothing built |

## Security documentation (Phase 2, as built)

| Document | Contents |
|----------|----------|
| [security/cryptographic-implementation.md](security/cryptographic-implementation.md) | Libraries/versions, parameters, envelope formats, documented deviations |
| [security/key-hierarchy.md](security/key-hierarchy.md) | Derivation tree, wrap records, key lifecycle |
| [security/recovery-implementation.md](security/recovery-implementation.md) | PMRK1 codec, workflows, forward-only revocation |
| [security/vault-header-format.md](security/vault-header-format.md) | 512-byte header table, pre/post-unlock authentication, crash-safe rewrites |
| [security/sqlcipher-integration.md](security/sqlcipher-integration.md) | Raw provider setup, pragmas, genuineness gates |
| [security/secure-memory-limitations.md](security/secure-memory-limitations.md) | Zeroization guarantees and GC-runtime limits |
| [security/cryptographic-test-results.md](security/cryptographic-test-results.md) | KAT/cross-validation results, benchmarks, residual risks |

## Unresolved items requiring verification (from the contract's Phase 0 mandate)

- **Q1** SQLCipher provider (`SQLitePCLRaw.bundle_e_sqlcipher`) + NSec/libsodium native assets on
  .NET 10 — Phase 2 spike must pass before crypto code lands (ADR-002).
- **Q2** Argon2id parameters need a recorded benchmark on target hardware (ADR-003).
- **Q3–Q6** zxcvbn port licensing, installer tech, code-signing cert, Persian wordlist — see
  product-requirements.md §8.
- Accepted residual risks R-1..R-10 are listed in threat-model.md §9; R-1 (memory scraping of an
  unlocked session) and R-8 (Windows clipboard history) are inherent and documented, not solved.
