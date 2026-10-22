# Release Build — as built (Phase 7)

## Build script

`build/release.ps1` — PowerShell script producing the full artifact set:
- `dotnet publish` (self-contained, win-x64 / win-arm64)
- Portable ZIP
- WiX v5 MSI installer (requires `dotnet tool install --global wix`)
- SHA256SUMS.txt
- release-manifest.json

## WiX installer

`installer/Ganjineh.Installer.wxs` — WiX v5 source. Build via `wix build` (dotnet tool).
The installer preserves user data (`Documents\Ganjineh`, `%APPDATA%\Ganjineh`) on uninstall.

## ARM64

NSec/libsodium and SQLitePCLRaw/e_sqlcipher provide win-arm64 native assets. The ARM64 build
is produced by the same release script but has not been smoke-tested on real ARM64 hardware
(no ARM64 test environment available) — documented as such in the release manifest.
