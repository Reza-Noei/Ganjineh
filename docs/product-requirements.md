# Product Requirements — Ganjineh (گنجینه) Offline Password Manager

Status: **Phase 0 — Proposed** | Owner: Architecture | Last updated: 2026-10-22

> "Ganjineh" (گنجینه, *treasure vault*) is a working title; the code namespace remains
> `Ganjineh.*` per the architecture contract. Rename is a cosmetic, low-risk change.

## 1. Vision

A Persian-first, fully offline Windows desktop password manager for a single user managing
multiple independent encrypted vaults. Security posture and recovery reliability are modeled on
Bitwarden/KeePassXC-class products; UX is modeled on 1Password-class polish, localized for
Right-to-Left Persian users first.

## 2. Goals / Non-Goals

**Goals**
- G1: Multiple fully independent vaults, each a separate security boundary (own DB, master
  password, recovery key, key hierarchy).
- G2: Offline recovery: a per-vault Recovery Key that unlocks the vault and resets a forgotten
  master password without any server or cloud.
- G3: Genuine SQLCipher encryption at rest, verified by test, not assumed from package name.
- G4: Persian-first RTL UX of premium quality; English localization; Light/Dark themes.
- G5: Both Installer and Portable distributions from one codebase and one vault format.
- G6: Honest security communication — documented residual risks, no fake guarantees.

**Non-Goals (v1)**
- No cloud sync, no server, no remote authentication, no telemetry, no mandatory networking.
- No browser extension, browser integration, or autofill.
- No passkeys (FIDO2) storage or management.
- No mobile/web clients.
- No password sharing/multi-user.
- No breach-database checks (no bundled dataset; no implicit external calls).

## 3. Personas

- **P1 — Iranian knowledge worker (primary):** Persian UI, RTL habits, stores work + personal
  credentials on a home PC or laptop. Moderate technical skill; expects recovery to "just work"
  if they lose the master password.
- **P2 — Traveler with a USB drive (portable):** runs from removable media on borrowed Windows
  machines; accepts reduced physical-security guarantees in exchange for availability.
- **P3 — Security-conscious evaluator:** audits the crypto claims, reads the threat model,
  checks that no plaintext leaks at rest.

## 4. Functional Requirements

IDs are stable and referenced by other docs and the test plan.

### 4.1 Vault lifecycle (FR-VL)
- FR-VL-1 Create vault with unique random Vault ID, own SQLCipher DB, own KDF salt/params.
- FR-VL-2 Open / Lock / Unlock / Close vault; unlock of one vault never affects another.
- FR-VL-3 Rename vault (display name stored encrypted inside the vault, not in any shared
  plaintext index; a local-only label in the vault registry is optional and flagged as such).
- FR-VL-4 Backup vault to an encrypted `.gvbak` file; validate integrity; restore on the same or
  another Windows machine.
- FR-VL-5 Delete vault with explicit typed confirmation; app must **not** claim guaranteed
  secure erasure on SSDs/COW filesystems (see threat-model.md R-6).
- FR-VL-6 Vault switching clears the previous session keyring and releases DB handles.
- FR-VL-7 Change master password (re-wraps VMK; data re-encryption not required, optional
  "deep rotation" available). See key-management.md §6.

### 4.2 Recovery (FR-RC)
- FR-RC-1 Recovery Key generated at vault creation: ≥256 bits entropy, human-readable encoding
  with version prefix and checksum; shown once, confirmation required (recovery-design.md).
- FR-RC-2 Unlock with Recovery Key when master password is forgotten.
- FR-RC-3 Reset master password using Recovery Key (same VMK, new password wrap).
- FR-RC-4 Rotate Recovery Key; old wrap record revoked; rotation must not affect vault data.
- FR-RC-5 UI must state plainly: anyone holding the Recovery Key can open the vault.
- FR-RC-6 No security questions, no hints, no "fake recovery".

### 4.3 Items (FR-IT)
- FR-IT-1 Item types: Login, Secure Note, Payment Card, Identity, Wi-Fi, API Credential,
  Software License, Recovery Codes; plus unlimited custom fields (text / hidden / URL / date).
- FR-IT-2 Unlimited items; folders (nested one level in v1), tags, favorites.
- FR-IT-3 Trash with restore and permanent delete.
- FR-IT-4 Item history: every save creates a version; view/restore previous versions.
- FR-IT-5 Encrypted attachments (any type, size warning >100 MB), stored as chunked envelopes.
- FR-IT-6 Full-text search over decrypted in-memory index (never persisted); filters by type,
  folder, tag, favorite, trash, weakness (Security Center link).

### 4.4 Password generator (FR-PG)
- FR-PG-1 Length 8–128; toggles for upper/lower/digits/symbols; per-set minimum counts.
- FR-PG-2 Character exclusion list; "avoid ambiguous" option (Il1|0Oo`'" etc.).
- FR-PG-3 Generation from OS CSPRNG (`RandomNumberGenerator`), unbiased selection.
- FR-PG-4 Passphrase mode: word list, 3–10 words, configurable separator and capitalization,
  optional digit injection; bundled word list must be license-clean (EFF-style; attribution).
- FR-PG-5 Strength estimation (zxcvbn-style offline heuristic or documented fallback), never
  involving network calls.

### 4.5 Authenticator (FR-TOTP)
- FR-TOTP-1 RFC 6238 TOTP codes; algorithms SHA-1/256/512; 6/7/8 digits; period 15–120 s
  (default SHA-1 / 6 / 30 s for compatibility).
- FR-TOTP-2 Base32 secrets (RFC 4648, tolerant of spaces/case); otpauth:// URI import.
- FR-TOTP-3 Live countdown ring; one-click copy (subject to clipboard policy).

### 4.6 Security Center (FR-SC)
- FR-SC-1 Offline reports: weak, reused, duplicate, empty, old (>365 days) passwords.
- FR-SC-2 Password health overview (counts + trend-free summary; no charts requiring history).
- FR-SC-3 All analysis in-memory during unlocked session; no persistent plaintext or unsalted
  hash of secrets.
- FR-SC-4 No breach-database claims; if a privacy-preserving external check is ever added it
  must be explicit, documented, and opt-in (v1: absent).

### 4.7 Backup / Import / Export (FR-BK)
- FR-BK-1 Manual and scheduled encrypted backups; retention policy (keep N).
- FR-BK-2 `.gvbak` integrity validation (AEAD tag + manifest SHA-256) before restore.
- FR-BK-3 Restore to a new vault ID on another device (no machine binding).
- FR-BK-4 Import: Bitwarden JSON export, KeePassXC CSV/XML, generic CSV with mapping wizard
  and dry-run preview. Minimal viable set confirmed in Phase 6.
- FR-BK-5 Encrypted native export; plaintext CSV export exists but demands typed confirmation
  and shows persistent warnings.
- FR-BK-6 All DB updates atomic (write-temp → flush-to-disk → replace); corruption detected via
  AEAD/manifest checks with guided recovery from backup.

### 4.8 UX (FR-UX) — details in ui-ux-specification.md
- FR-UX-1 Persian (fa) neutral locale; English (en) satellite; runtime switch.
- FR-UX-2 Light/Dark themes; RTL three-column layout (right nav / center list / left details).
- FR-UX-3 Secrets hidden by default; reveal is explicit with auto-re-hide; copy subject to
  clipboard expiry (default 60 s, 10–120 s configurable) and clear-on-lock.
- FR-UX-4 Auto-lock on idle (default 10 min) and on Windows session lock.
- FR-UX-5 Keyboard navigation throughout; accessible contrast (WCAG AA for text).

## 5. Non-Functional Requirements

| ID | Requirement | Target |
|----|-------------|--------|
| NFR-1 | Vault unlock latency (warm) | ≤ 2.0 s p95 on ≥2018 hardware incl. Argon2id |
| NFR-2 | Search latency, 5 000 items | ≤ 100 ms p95 after unlock |
| NFR-3 | Cold start to vault picker | ≤ 3 s |
| NFR-4 | Argon2id unlock cost | 0.3–1.0 s target band; re-tuned per hardware class |
| NFR-5 | Crash safety | No plaintext secrets in dumps; dumps disabled by default |
| NFR-6 | Data-at-rest | SQLCipher 4 + app-layer AEAD envelopes; verified by test |
| NFR-7 | Portability | Portable dir relocatable across machines; no registry secrets |
| NFR-8 | Localization coverage | 100% of user-facing strings in fa and en; no hard-coded text |

## 6. Distribution

- **Installer:** WiX v5 MSI (final packaging tech confirmed in Phase 7); installs binaries under
  Program Files; vaults under `%USERPROFILE%\Documents\Ganjineh\Vaults` (user-moveable);
  uninstall preserves vaults; upgrade preserves data.
- **Portable:** single zip; no admin rights; `<root>\Data` holds vaults + settings; relocatable;
  handles read-only media (graceful degradation) and unsafe removal (atomic writes).
- Both share identical vault format, master password and recovery workflows. DPAPI is never the
  sole protector of any vault (ADR-006).

## 7. Release Criteria (v1)

1. All FR items implemented and mapped to passing tests (security-test-plan.md).
2. Security test plan executed; no open High/Critical findings; residual risks documented.
3. Argon2id parameters benchmarked on target hardware class and documented.
4. SQLCipher genuineness test (open encrypted DB with plain SQLite must fail) green.
5. Recovery workflow E2E verified, including forgotten-password reset and rotation.
6. Portable vault moved to a second machine and opened successfully.
7. fa/en localization complete; RTL layout reviewed on both themes.
8. Docs finalized: threat model, crypto spec, recovery guide, residual-risk register.

## 8. Open Questions (Phase 0)

| Q | Question | Blocked on |
|---|----------|-----------|
| Q1 | ~~SQLCipher provider on .NET 10~~ **Closed (Phase 2):** raw SQLitePCL + e_sqlcipher 2.1.11; Microsoft.Data.Sqlite rejected (battery race yields unencrypted engine — spike-verified). Zetetic remains a drop-in fallback. | — |
| Q2 | ~~Argon2id baseline on target hardware class~~ **Closed (Phase 2):** m=128 MiB, t=3, p=1 ≈ 172 ms dev / 0.35–0.7 s min-spec projection; docs/security/cryptographic-test-results.md | — |
| Q3 | zxcvbn C# port license/vetting vs in-house estimator | Phase 5 |
| Q4 | Installer tech: WiX v5 MSI vs Inno Setup | Phase 7 |
| Q5 | Code signing certificate availability/cost | Release |
| Q6 | Persian passphrase word list quality | Phase 5 (v1 ships EFF list) |
