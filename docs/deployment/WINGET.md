# WinGet

Package identifier: **`RezaNoei.Ganjineh`** (publisher "Reza Noei" — from the repository owner;
no invented legal identity). Manifests live in `winget/manifests/RezaNoei.Ganjineh/<version>/`
in this repository: ` installer.yaml`, `defaultLocale.yaml` (en-US), `locale.fa-IR.yaml`.

## Metadata truthfulness

- License: **Proprietary** (the repository currently has no LICENSE file — this is the truthful
  declaration). If a LICENSE (e.g. MIT) is added, update `License` + `LicenseUrl` in the
  manifests and in the release notes.
- InstallerType: `exe`, silent switches `-s` / `--silent`, per-user scope, no elevation.
- InstallerUrl points at the version-tagged GitHub Release asset
  `https://github.com/Reza-Noei/Ganjineh/releases/download/v<version>/Ganjineh-win-Setup.exe`
  (immutable — release tags are never rewritten).
- `InstallerSha256` must equal the SHA-256 of that exact asset (see the release's
  SHA256SUMS.txt).

## Submitting a new version

1. Publish the GitHub Release (docs/deployment/RELEASE_PROCESS.md).
2. Copy the previous manifest folder to the new version directory; update `PackageVersion`,
   `InstallerUrl`, `InstallerSha256`, `ReleaseDate`, `ReleaseNotes`/`ReleaseNotesUrl`.
3. Validate:

   ```powershell
   winget validate --manifest winget/manifests/RezaNoei.Ganjineh/<version>
   ```

4. Submit to `microsoft/winget-pkgs`:

   ```powershell
   gh auth login            # once; browser flow
   gh repo fork microsoft/winget-pkgs --clone
   # copy the version folder into manifests/r/RezaNoei/Ganjineh/<version>/, commit, push
   gh pr create --repo microsoft/winget-pkgs --head <you>:manifest-ganjineh-<version> `
     --title "New version: RezaNoei.Ganjineh version <version>" `
     --body "Automatic submission — adds RezaNoei.Ganjineh version <version>."
   ```

5. The winget-pkgs validators run on the PR; expect automated review comments.

## Status

- Manifests for 1.0.0: generated + `winget validate` — see the release report.
- Catalog presence is claimed ONLY after the winget-pkgs PR merges. Until then Ganjineh is
  installable via the GitHub Release directly.
