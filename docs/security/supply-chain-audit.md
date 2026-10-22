# Supply Chain Audit — Phase 8

## Direct dependencies (production)

| Package | Version | License | Maintainer | Status | Notes |
|---------|---------|---------|-----------|--------|-------|
| NSec.Cryptography | 26.4.0 | MIT | Klaus Hartke | Active | Binds libsodium 1.0.22 |
| libsodium (native) | 1.0.22 | ISC | jedisct1 | Active | Audited widely |
| SQLitePCLRaw.core | 2.1.11 | Apache 2.0 | ericsink | Active | |
| SQLitePCLRaw.lib.e_sqlcipher | 2.1.11 | BSD-style | ericsink | Active | SQLCipher 4.x community |
| SQLitePCLRaw.provider.e_sqlcipher | 2.1.11 | Apache 2.0 | ericsink | Active | |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | Microsoft | Active | |
| Microsoft.Extensions.DependencyInjection | 10.0.13 | MIT | Microsoft | Active | |
| zxcvbn-core | 7.0.92 | MIT | trichards57 | Active | Strength estimation |

## Direct dependencies (test-only, not shipped)

| Package | Version | License | Purpose |
|---------|---------|---------|---------|
| BouncyCastle.Cryptography | 2.7.0 | MIT | Argon2id cross-validation |
| xUnit | 3.x | Apache 2.0 | Test framework |
| FlaUI.UIA3 | 5.0.0 | MIT | UI automation |

## Transitive dependencies

| Package | Version | Source |
|---------|---------|--------|
| libsodium | 1.0.22 | NSec (native binding) |
| Microsoft.Data.Sqlite (NOT used) | — | removed (battery race issue) |
| Microsoft.Extensions.DependencyInjection | 10.0.13 | MEF/MSBuild |

## Known advisories

None identified in current versions at time of review.

## Pinning and reproducibility

- All package versions pinned via `<PackageReference>` (no floating versions).
- Restore uses `nuget.org` only (no custom feeds).
- The `dotnet restore --lock-file` pathway is available but lock files are not committed
  (reproducibility limitation: package sources may change content over time).

## Bundled assets

| Asset | License | Source |
|-------|---------|--------|
| Vazirmatn font (Regular/Medium/SemiBold) | SIL OFL 1.1 | github.com/rastikerdar/vazirmatn |
| EFF Large Wordlist (7,776 words) | CC BY 4.0 | eff.org/dice |

## Dependency security review

- NSec (libsodium wrapper) and Microsoft.Data.Sqlite (e_sqlcipher) were both evaluated against
  known CVEs — no known vulnerabilities in these versions at time of review.
- The `Microsoft.Data.Sqlite` package was **removed** due to a battery-race issue that could
  silently yield an unencrypted engine (documented in ADR-002 and sqlcipher-integration.md).
- No deprecated or unmaintained packages are in the dependency graph.
