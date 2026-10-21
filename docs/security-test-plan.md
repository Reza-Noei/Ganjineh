# Security Test Plan — Ganjineh

Status: **Phase 0 — Proposed** | xUnit everywhere; FlaUI + UI Automation 3 for WPF E2E.
Test IDs (ST-*) are referenced by every other doc; a phase exits only when its mapped IDs pass.

## 1. Levels and Tooling

| Level | Tooling | Scope |
|-------|---------|-------|
| Unit | xUnit | Domain, crypto primitives, codecs, generators |
| Known-answer (KAT) | xUnit, RFC/NIST vectors | Proves primitive bindings, not just compile |
| Negative/adversarial | xUnit | Tamper, splice, truncation, malformed input |
| Integration | xUnit, real files in temp dirs | Vault lifecycle, atomic writes, migrations, restore |
| At-rest verification | xUnit + raw `e_sqlite3` (unkeyed) + byte scans | Proves encryption actually on |
| UI E2E | FlaUI (UIA3) | Persian RTL flows, keyboard navigation, clipboard TTL |
| Fuzz | Deterministic corpus fuzzer (xUnit-driven, seeded) | Header/envelope/attachment parsers |
| Benchmark | xUnit `#if BENCH` + console runner | Argon2id tuning, unlock/search latency |

No cryptographic mocks in production code paths — SecurityTests run against the real crypto
module only; any test double of crypto exists solely in Desktop.UITests for speed, is visibly
named `FakeCryptoForUiTestsOnly`, and never ships.

## 2. Known-Answer Tests (ST-KAT)

| ID | Vectors |
|----|---------|
| ST-KAT-1 | HKDF-SHA-256: RFC 5869 A.1–A.3 |
| ST-KAT-2 | Argon2id: RFC 9106 §5.3 reference vectors through our KDF wrapper |
| ST-KAT-3 | AES-256-GCM: NIST CAVS encrypt/decrypt incl. modified-tag rejection |
| ST-KAT-4 | TOTP: RFC 6238 Appendix B (SHA-1/256/512); 6-digit cross-check; window/resync |
| ST-KAT-5 | Base32: RFC 4648 §10; Crockford homoglyph mapping table |
| ST-KAT-6 | Recovery Key codec: encode→decode round-trips, checksum rejection, case/spacing tolerance |
| ST-KAT-7 | Golden envelopes: pinned header + item + attachment fixtures (format regression) |

## 3. Security / Adversarial Tests (ST-SEC)

| ID | Scenario | Expected |
|----|----------|----------|
| ST-SEC-1 | Wrong master password, wrong recovery key, swapped credentials across the two wraps | Generic `AuthenticationFailed`; identical error+timing shape; no partial state |
| ST-SEC-2 | Recovery unlock success; recovery failure path | Open vs. reject as specified (recovery-design §4.5) |
| ST-SEC-2b | Recovery rotation: old key fails, new succeeds, data intact, counter bumped | Rotation contract |
| ST-SEC-2c | Recovery reset of master password: old MP fails after; VMK/data unchanged | Reset contract |
| ST-SEC-3 | Ciphertext tamper (flip ct/tag byte), header tamper (each field), wrong-Vault-ID splice (envelope moved between vaults/items/purposes) | All fail authentication; vault refuses open or marks record invalid; no crash |
| ST-SEC-4 | Backup corruption: truncated, tag-flipped, manifest-swapped, body replaced with older backup | Refuse restore; original vault untouched |
| ST-SEC-5 | Lock behavior: after lock, keyring buffers zeroized (test accessor), UI state cleared, DB closed | No residual plaintext via accessor scan |
| ST-SEC-6 | KDF param downgrade edit → open attempt | AEAD failure (pre-unlock); floor-violation warning path |
| ST-SEC-7 | Unicode master passwords (Persian, emoji, ZWNJ, RTL marks) round-trip unlock | Deterministic normalization policy; success |
| ST-SEC-8 | Clipboard expiry: copy secret, advance clock, verify cleared; ownership check with foreign clipboard content | TTL honored; foreign content untouched |
| ST-SEC-9 | Secret leakage scan: run full workflow with sentinel secrets; grep logs, exception texts, settings, memory snapshots of UI state | Zero sentinel occurrences |
| ST-SEC-10 | Vault deletion: confirmation required; refuse to claim erasure (message audit) | Files removed best-effort; copy remains (documented) |

## 4. At-Rest Verification (ST-DB)

| ID | Scenario |
|----|----------|
| ST-DB-1 | Open `vault.db` with plain unkeyed SQLite → must fail (not "opens empty") |
| ST-DB-2 | Byte-scan `vault.db`, WAL, `-shm`, temp/export files for sentinel plaintext | zero hits |
| ST-DB-3 | `cipher_version` empty ⇒ app refuses open (simulated provider swap) |
| ST-DB-4 | Registry (`vaults.json`) contains no sensitive metadata (schema assertion) |

## 5. Integration (ST-INT)

| ID | Scenario |
|----|----------|
| ST-INT-1 | Vault isolation: unlock A, assert no keyring/DB handle for B; unlock B with different MP; concurrent sessions never share state |
| ST-INT-2 | Two vaults, identical MP: distinct salts, wraps, DB bytes; DB from A won't open under B's keying |
| ST-INT-5 | Unicode MP vault round-trip |
| ST-INT-6 | Atomic header writes: simulate crash between temp-write and replace (fault injection) → original intact |
| ST-INT-7 | Interrupted DB write: kill process mid-transaction (WAL) → reopen consistent; no plaintext in WAL |
| ST-INT-8 | Cross-device restore: `.gvbak` → new machine (new vault ID) opens with same MP |
| ST-INT-9 | Migration failure: injected fault mid-migration → original vault byte-identical, app reports cleanly |
| ST-INT-10 | Portable vault moved to second machine/user profile → opens |
| ST-INT-11 | Master password change: new MP opens, old fails, history/attachments intact |

## 6. UI / E2E (ST-UI, FlaUI + UIA3)

| ID | Scenario |
|----|----------|
| ST-UI-1 | Onboarding → create vault → recovery setup confirmation → land in shell (fa, RTL) |
| ST-UI-2 | Recovery skip impossible; confirmation challenge gates creation |
| ST-UI-3 | Persian RTL layout audit (nav right, details left) in Light+Dark; screenshot baselines |
| ST-UI-4 | Keyboard-only: navigate, create item, copy, reveal, lock (`Ctrl+L`) |
| ST-UI-5 | Clipboard TTL observed via UI flow; lock clears revealed fields |
| ST-UI-6 | Language switch fa↔en live; no untranslated strings (automation scans all visible texts) |
| ST-UI-7 | Mixed-direction rendering: Persian title containing Latin URL/username displays uncorrupted (text-format assertions) |

## 7. Fuzzing (ST-FZ)

| ID | Target | Method / acceptance |
|----|--------|---------------------|
| ST-FZ-1 | `HeaderCodec.Parse` | 10⁶ seeded mutations of valid header + structured corpora (truncations, field flips, boundary lengths); acceptance: typed parse error or auth failure only; **no unhandled exception, no allocation >1 MiB from any single input** |
| ST-FZ-2 | `EnvelopeCodec` | same method over envelope bytes |
| ST-FZ-3 | Attachment chunk reader | truncation/truncation-after-header/index gaps |
| ST-FZ-4 | Import parsers (Phase 6) | malformed CSV/JSON, path-traversal filenames (`..\`, absolute, ADS `:`, reserved names), zip-bomb-shaped sizes |

## 8. Benchmarks (non-security gate, recorded per release)

- Argon2id median/p95 across min-spec and dev machines → parameter decision record (Q2).
- Unlock p95 ≤ 2 s; search 5k items ≤ 100 ms; attachment stream throughput sanity.

## 9. Release Gate (Phase 8)

Every ST-* ID green; fuzz corpora + seeds archived with the release; residual-risk register
(threat-model.md §9) reviewed and signed off in the final report; no open Critical/High.
