# Release Process

## Publishing v1.1.0 (example) — the whole procedure

1. **Bump the version** — the single source of truth:

   ```xml
   <!-- src/Ganjineh.Desktop/Ganjineh.Desktop.csproj -->
   <Version>1.1.0</Version>
   ```

2. **Commit + push to `main`:**

   ```powershell
   git add -A; git commit -m "v1.1.0"; git push Ganjineh main
   ```

3. **Tag and push the tag:**

   ```powershell
   git tag v1.1.0
   git push Ganjineh v1.1.0
   ```

4. **Done.** GitHub Actions builds, tests, packs, and publishes
   `https://github.com/Reza-Noei/Ganjineh/releases/tag/v1.1.0`.
   Monitor the run under the repository's **Actions** tab.

## Rules

- SemVer only (`X.Y.Z`). Tag must equal `v` + csproj `<Version>`.
- The workflow **fails** if the release already exists — never overwrite; bump instead.
- If CI fails: fix, push a new commit, delete the failed tag if you created it
  (`git tag -d vX; git push Ganjineh :refs/tags/vX`), re-tag on the new commit.
- Re-running a partially uploaded release: delete the release (only you can), then re-tag.

## Emergency: manual local release (CI unavailable)

```powershell
.\build\release.ps1                 # publishes + vpk pack + MSI + SHA256SUMS
vpk upload github --outputDir artifacts\release\velopack `
  --repoUrl https://github.com/Reza-Noei/Ganjineh `
  --token <PAT with contents:write> --publish --releaseName "Ganjineh vX" --tag vX
gh release upload vX artifacts\release\SHA256SUMS.txt artifacts\release\Ganjineh-Setup-x64-X.msi artifacts\release\velopack\Ganjineh-win-Portable.zip --clobber
```

Requires `gh` authenticated (or a PAT); the CI path needs none of this.
