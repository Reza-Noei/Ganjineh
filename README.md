# Ganjineh (گنجینه)

Offline, Persian-first, multi-vault password manager for Windows — WPF on .NET 10.
Design documentation lives in [`docs/`](docs/README.md) (Phase 0 output; the implementation
follows the phased roadmap there).

## Status

Phase 1 — solution skeleton, DI composition, RTL WPF shell, fa/en localization, Light/Dark themes.

## Build and test

Requires the .NET 10 SDK.

```powershell
dotnet build
dotnet test          # unit + integration tests
dotnet test tests/Ganjineh.Desktop.UITests   # FlaUI UI smoke (launches the app briefly)
```

Run the app:

```powershell
dotnet run --project src/Ganjineh.Desktop
```

## Layout

- `src/` — Domain, Application, Cryptography, Persistence, Infrastructure, Desktop
  (dependency rules in `docs/architecture.md`)
- `tests/` — per-layer unit tests, IntegrationTests, SecurityTests, Desktop.UITests
- `docs/` — threat model, cryptographic specification, ADRs, roadmap, security test plan
