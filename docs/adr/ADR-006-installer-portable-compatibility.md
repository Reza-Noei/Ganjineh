# ADR-006: Installer / Portable Compatibility

Status: Proposed | Date: 2026-10-22 | Decides: contract §6

## Context
Two distributions must coexist from one codebase and one vault format: an installed product
(managed lifecycle, per-user data under the profile) and a portable build (USB drives, no admin,
no registry secrets, machine-independent).

## Decision
- **Identical vault format and workflows in both modes**; the only difference is where
  *application state* lives: `%APPDATA%\Ganjineh\` (installer) vs `<portable root>\Data\`
  (portable). Mode is detected by presence of a `Data` directory next to the executable
  (portable marker), never by registry inspection.
- Vault directories are relocatable by design (`<VaultId>.pvault` self-contained,
  vault-file-format.md §1); no machine binding anywhere in the crypto (no DPAPI in the vault
  path — **DPAPI is never the sole protector**, and v1 uses DPAPI for nothing security-critical
  at all).
- Settings and registry (`vaults.json`) contain no secrets (schema asserted by ST-DB-4).
- Uninstall never touches vaults (explicit in installer scripts + tested); upgrade preserves
  data; backup guidance shown before uninstall and before migrations.
- Portable specifics: atomic-write helper tolerates unsafe removal (temp+replace, flush-to-disk);
  read-only media → app runs with clear "cannot save" state rather than partial writes;
  removable-theft and residual-plaintext warnings shown on first portable run and in docs.

## Alternatives Considered
- **DPAPI-protected "convenience unlock" (Windows Hello-style cached credential)** — deferred to
  future phase with hardware-backed keys; rejected for v1 because it binds vaults to machines and
  complicates the portable story.
- **Single-file vault containers** — nicer "one file to carry" UX but couples header+DB into a
  custom container format v1 doesn't need; directory-per-vault wins on atomicity and simplicity.

## Consequences
- One code path, two data roots — exercised by ST-INT-10 (move portable to second machine).
- Windows Hello / machine-bound convenience remains a clean future extension (contract §10)
  because nothing in v1 assumes user-profile-bound secrets.
