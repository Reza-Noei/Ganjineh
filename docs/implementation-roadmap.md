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

## Progress Log

- **2026-10-22 — Phase 0** delivered and approved by the user.
- **2026-10-22 — Phase 1 complete.** 13-project skeleton (`PasswordManager.slnx`) with the
  documented dependency rules; DI composition root in Desktop; three-column RTL shell
  (nav/list/details placeholders); fa-neutral + en-satellite localization with live language and
  FlowDirection switching; Light/Dark theme dictionaries with live swap; Vazirmatn vendored
  (OFL). Zero-warning build (`TreatWarningsAsErrors`). Tests: 18/18 green — 5 Domain,
  2 Application, 10 Integration (localization parity, theme parity, settings store incl.
  portable detection), 1 FlaUI UIA3 smoke (launch → Persian nav → live EN switch → theme swap).
  *Deferred to later phases, by design:* Lucide icon set (nav is text-only in the skeleton),
  real navigation content, clipboard service (lands with item actions, Phase 3+).
- **2026-10-22 — Phase 2 complete.** Cryptographic engine (AES-256-GCM envelopes, HKDF-SHA-256
  domain separation, Argon2id KDF), VMK wrapping (password + recovery paths), Recovery Key
  system (generation/PMRK1 codec/unlock/reset/rotation), 512-byte authenticated vault header
  (strict codec, MAC, anti-rollback counter), raw e_sqlcipher persistence with identity
  binding and genuineness gates, `VaultSession` lifecycle with zeroizing keyring, atomic
  crash-safe header rewrites with fault-injection tests. Q1/Q2 closed; deviations documented
  (engine parallelism p=1, wrap-AAD Revision 1, raw SQLitePCL instead of Microsoft.Data.Sqlite).
  **Tests: 103/103 green** (Domain 5, Application 2, Cryptography 60 incl. KATs + BouncyCastle
  cross-validation + benchmarks, Persistence 4, Integration 24, Security 7, UI smoke 1).
  Docs: docs/security/* (7 files). *Deferred by design:* attachment chunk envelopes (Phase 4),
  backup `.gvbak` writer (Phase 6), UI integration of unlock flows (Phase 3).
- **2026-10-22 — Phase 3 complete.** Vault management (picker, guided creation with recovery
  challenge, unlock password/recovery, lock/switch/rename/remove/delete, auto-lock + session-lock),
  login-credential CRUD over a Phase-4-ready item schema (key-free repository, envelope payloads,
  versioned rows), debounced Persian-normalized search with password-free in-memory index,
  ownership-safe clipboard with 30 s expiry, full three-column RTL workspace with localized
  fa/en flows. **Tests: 113/113 green** including a 22 s FlaUI E2E of the complete user journey.
  Docs: docs/phase-3-*.md + vault-management/credential-management/search-design/clipboard-security/
  session-lifecycle. *Deferred by design:* favorites/folders/tags/trash/history UI (Phase 4),
  generator/TOTP/security-center screens (Phase 5), settings screens beyond defaults (Phase 7).
- **2026-10-22 — Phase 4 complete.** Unified item architecture (9 kinds, payload v1 with
  custom-fields engine, v0-compat deserializer — additive schema migration to v2), folders/tags
  (encrypted names, cycle-safe, cascade semantics), favorites, encrypted version history with
  retention + restore, trash with restore/purge/empty, streamed chunked-AEAD attachments
  (4 MiB chunks, TLS-1.3-style nonces, terminator auth, failure-safe ordering), advanced search
  across kinds with folder/tag/favorite filters, and the extended Persian RTL UI (kind sections,
  folder/tag panels, dynamic metadata-driven editor and details, attachments/history/trash
  panels). **Tests: 137/137 green** (65 Cryptography incl. 5 chunked-AEAD protocol tests,
  53 Integration incl. 19 Phase 4, UI E2E still green). Docs: docs/phase-4-*.md +
  vault-item-model/custom-fields/folders-and-tags/item-history/trash-management/
  attachment-encryption/advanced-search. *Deferred by design:* TOTP, generator, security center
  (Phase 5); reusable custom-item templates and history diffing UI (later); backup/import
  (Phase 6).
- **2026-10-22 — Phase 5 complete.** CSPRNG password generator (rejection sampling, unbiased
  Fisher–Yates, presets/constraints/exclusions, statistical distribution test), EFF-large-
  wordlist passphrase generator (integrity-checked, CC BY), zxcvbn-core strength estimator with
  Persian feedback mapping, RFC 4226/6238 HOTP/TOTP engine (all Appendix B/D vectors green) with
  encrypted per-item TOTP config, otpauth import, live codes UI with countdown, and the offline
  Security Center (weak/reused/empty/old/missing-TOTP/duplicate/expiring findings with
  documented severities, session-bound analysis, Persian RTL dashboard). **Tests: 195/195 green**
  (Cryptography 104 incl. all RFC vectors + generator statistics, Integration 72 incl. 19
  Phase 5). Docs: docs/phase-5-*.md + password-generator/passphrase-generator/
  password-strength-estimation/totp-authenticator/hotp-authenticator/security-center/
  security-analysis-privacy. *Deferred by design:* QR-image import, Persian passphrase wordlist,
  HOTP item UI; backup/restore + import/export (Phase 6).
- **2026-10-22 — Phase 8 (adversarial review) complete.** Adversarial integration tests (header
  bit-flip fuzz 2000 cases, at-rest sentinel scan, chunk tag tamper, backup entry traversal,
  KDF resource exhaustion, nonce uniqueness 10 k, cross-vault selective restore, stale-session
  operations). Four defects found and fixed during Phase 6 hardening (F-01–F-04 in
  security-findings.md). Security findings register, final threat model, cryptographic audit,
  memory security audit, supply chain audit, penetration test report, residual risk assessment,
  and release readiness assessment (Conditional Go) produced. See docs/security/* and
  docs/phase-8-test-results.md.
- **2026-10-23 — Material Design adoption + RTL layout fixes.** Replaced the Phase-1
  hand-rolled Fluent-inspired styles with Material Design 3 (MaterialDesignThemes 5.3.2):
  `BundledTheme` (Blue/LightBlue, live Light/Dark rebuild in ThemeService), MD3 default control
  styles rebased under Styles/Controls.xaml, Material PackIcon navigation/toolbar iconography,
  Vazirmatn wired into Material's font resource. Brand tokens remapped to the Material palette
  (+ `Overlay.Scrim`). Fixed RTL overlap/clipping defects: Unlock label/password stacked in one
  grid cell, duplicated search header in the item-list card, physical left-margins that collapsed
  gaps under RTL (search/add, folder/tag +/-, detail copy rows — now flow-neutral symmetric
  margins), editor/generator/delete overlays now scroll instead of clipping, and
  direction-dependent Left/Right alignments removed. Added `Generator.Length` fa/en key
  (replaces hard-coded "طول"). **Tests: UI E2E green; full solution builds 0 warnings.**
  Docs: ui-ux-specification.md §2 rewritten.
