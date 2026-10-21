# Implementation Roadmap — Phases and Exit Criteria

Status: **Phase 0 — Proposed** | Per-phase workflow is fixed by the contract (§12): inspect repo →
review architecture → state scope → implement → test → build → run tests → fix → document → report.

## 0. Environment (verified 2026-10-22)

- .NET SDK **10.0.400** present; WindowsDesktop runtime 10.0.11 present. ✔
- Windows 11 Enterprise 22621. ✔
- To verify at Phase 2 start: SQLCipher-capable provider package restore on .NET 10 (Q1),
  NSec/libsodium win-x64 native assets (Q1).

## Phase Plan

| Phase | Scope | Key deliverables | Exit criteria (tests gate) |
|-------|-------|------------------|---------------------------|
| **0** — Design (this delivery) | Threat model, ADRs, specs | 11 docs + 7 ADRs | User approval |
| **1** — Skeleton | Solution, DI, WPF shell, fa/en + themes, CI script (local) | Builds; shell renders RTL both themes; localization infra | UI smoke: theme+language switch; zero warnings policy baseline |
| **2** — Crypto engine | Argon2id, HKDF domains, AEAD envelopes, keyring, recovery codec, SQLCipher provider + raw-key pragma | Cryptography + Persistence cores | All KATs green (ST-KAT-*); ST-DB-1..3; wrong-password/tamper negatives (ST-SEC-1,3,4); Argon2 benchmark recorded (Q2 closed) |
| **3** — Vault lifecycle + core CRUD | Create/open/lock/unlock/close/rename; header write; registry; items+folders+tags+search+trash+history | First real vault round-trip | ST-INT-1,2 (isolation); ST-INT-5 (unicode MP); ST-INT-6/7 (atomic writes); wrong-vault-ID splice fails |
| **4** — Item depth | Attachments (chunked), item types, custom fields, favorites, filters | Full §7 core matrix usable | ST-SEC-4a (attachment tamper/truncate); history restore; large-attachment stream test |
| **5** — Generator, TOTP, Security Center | FR-PG, FR-TOTP, FR-SC offline reports | Feature-complete daily driver | TOTP RFC vectors; generator distribution sanity; SC report correctness on fixture vault |
| **6** — Backup/Import/Export + recovery verification | `.gvbak`, restore, importers, deep rotation, recovery E2E | Data safety story complete | ST-INT-8 (cross-device restore), ST-SEC-2b/c (recovery rotation/reset), ST-SEC-4 (backup corruption) |
| **7** — Packaging + hardening | Installer (Q4), portable zip, clipboard TTL hardening, file associations, autolock-on-session-lock | Two distributables | Portable move to 2nd machine (ST-INT-10); uninstall keeps vaults; unsafe-removal drill |
| **8** — Security hardening + release | Full security-test-plan execution, fuzz, docs, residual-risk sign-off | Release candidate | Release criteria §7 of product-requirements.md all green |

Dependency notes: Phase 3 depends on 2 (keys); 4–6 depend on 3 (lifecycle); 7 needs 6 (backup
before uninstall/upgrade flows). UI shells for each feature arrive with the phase that builds
the feature — no separate UI phase at the end.

## Traceability (contract §7/§11 → phases)

| Contract area | Phase |
|---------------|-------|
| Multi-vault lifecycle (§2) | 3, tested 3/6 |
| Key hierarchy + recovery (§3/§4) | 2 (crypto) + 3 (lifecycle) + 6 (rotation E2E) |
| Storage guarantees (§5) | 2, verified continuously in 3–8 |
| Installer/Portable (§6) | 7 |
| Domain features (§7) | 3–6 |
| WPF UX (§8) | 1 (shell) then per-feature |
| Verification matrix (§11) | security-test-plan.md, mapped per phase exit |

## Standing Rules

1. No phase advances with failing tests or unimplemented contract items — gaps are reported, never skipped silently.
2. Every phase ends with: functionality report, outstanding risks, updated docs, next-step proposal.
3. Security-sensitive changes require the relevant doc section update **in the same phase**.
4. Any crypto-visible change (format, params, primitives) requires a new/updated ADR before code.
