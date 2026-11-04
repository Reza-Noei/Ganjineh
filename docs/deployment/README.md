# Deployment & Distribution — as built (2026-11-04)

Ganjineh ships from this repository. Nothing else hosts releases.

## Channels

| Channel | Artifact | Install model | Self-update |
|---|---|---|---|
| **Velopack (primary)** | `Ganjineh-win-Setup.exe` | Per-user, no admin (`%LOCALAPPDATA%\Ganjineh`) | Yes — delta updates from GitHub Releases |
| Portable | `Ganjineh-win-Portable.zip` | Unpack & run | No |
| WiX MSI (alternative) | `Ganjineh-Setup-x64-<v>.msi` | Per-machine, admin | No |

All three are assets of the same GitHub Release `vX.Y.Z`.

## Pipeline

`GitHub Actions` (`.github/workflows/release.yml`) on `windows-latest`:
build → test (unit + integration) → publish (self-contained win-x64) → `vpk pack` → WiX MSI →
SHA256SUMS → release-notes from commit history → guard against existing release →
`vpk upload github --publish` → upload auxiliary assets → validate feed → report URL.

## Update trust model

- Velopack verifies every downloaded package against the feed's SHA-256 metadata.
- **Publisher authenticity (Authenticode) is NOT yet configured.** The pipeline accepts a
  `VPK_SIGN_PARAMS` secret (signtool arguments) and signs when present.
- Because authenticity is not yet established, the app **never downloads or installs updates
  silently** — the startup check only *notifies*, and download + install are explicit user
  actions in Settings → Updates.
- The older Ed25519-signed *offline* update channel (`UpdateVerificationService`,
  docs/offline-update-design.md) is unaffected and remains for user-managed package transfer.

## Files

- `GITHUB_ACTIONS.md` — what the workflow does, inputs, secrets
- `RELEASE_PROCESS.md` — publishing a new version, end to end
- `VELOPACK.md` — app integration, update UX, data locations
- `CODE_SIGNING.md` — current state + how to enable Authenticode
- `WINGET.md` — manifests and the submission process
- `TROUBLESHOOTING.md` — failure modes and recovery

## Data locations (survive updates and uninstall)

- Vaults: `Documents\Ganjineh\`
- Settings + vault registry: `%APPDATA%\Ganjineh\`
- Application (Velopack): `%LOCALAPPDATA%\Ganjineh\` — replaceable, never contains user data
