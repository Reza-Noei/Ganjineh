# GitHub Actions — Release Workflow

Workflow: `.github/workflows/release.yml`, single job on `windows-latest`.

## Triggers

| Trigger | Behavior |
|---|---|
| Push tag `vX.Y.Z` | Releases exactly that version |
| `workflow_dispatch` | Uses the `version` input if given, else `<Version>` from `Ganjineh.Desktop.csproj` |

Never runs on pull requests. `permissions: contents: write` (minimum needed to create releases).

## Steps (in order)

1. Checkout (`fetch-depth: 0` — release notes need history).
2. .NET 10 SDK (`actions/setup-dotnet@v4`).
3. Version resolution: tag → input → csproj `<Version>` (single source of truth).
4. NuGet cache, restore, `dotnet build -c Release`.
5. `dotnet test` — unit + integration only (`--filter "FullyQualifiedName!~Desktop.UITests"`;
   the FlaUI suites need an interactive desktop session and are run on real machines, off-CI).
6. `dotnet publish` win-x64 self-contained, version-stamped (`-p:Version=`).
7. `vpk pack` (Velopack CLI installed in-step) → setup + full nupkg + portable + feed.
8. WiX MSI build.
9. Optional Authenticode signing — only when the `VPK_SIGN_PARAMS` secret exists.
10. `SHA256SUMS.txt` over setup + portable + MSI.
11. Release notes generated from `git log <prev-tag>..HEAD` (no fabricated changelog).
12. **Overwrite guard**: fails if release `vX.Y.Z` already exists.
13. `vpk upload github --publish` with `GITHUB_TOKEN` — creates the release and uploads the
    Velopack feed assets.
14. `gh release upload` — portable ZIP, MSI, checksums.
15. Validation: release exists, not draft, `releases.win.json` feed present.
16. Reports the release URL via `::notice::`.

## Secrets

| Secret | Required | Purpose |
|---|---|---|
| none | — | `GITHUB_TOKEN` (automatic) covers everything today |
| `VPK_SIGN_PARAMS` | optional | signtool arguments for Authenticode (see CODE_SIGNING.md) |

## Concurrency

`group: release-<ref>`, not cancelled — two simultaneous releases queue instead of racing.
