# Installer Design — WiX v5 MSI

## Technology selection

WiX v5 chosen for: .NET 10 compatibility, offline installation, reliable major upgrades,
standard uninstall/repair, and MSBuild integration (`dotnet build installer.wixproj`).

## Layout

```
installer/
  Ganjineh.Installer.wxs    WiX source
  build-installer.ps1        build script (candle + light via wix dotnet tool)
```

## Behavior

- Installs to `Program Files\Ganjineh\` (per-machine, requires admin for install only).
- Start Menu shortcut (optional Desktop shortcut via feature).
- Registers Add/Remove Programs entry with version, publisher, and uninstall string.
- **Upgrade**: MajorUpgrade element with `AllowSameVersionUpgrades=no` — clean upgrade path.
- **Uninstall**: preserves `%USERPROFILE%\Documents\Ganjineh\` and `%APPDATA%\Ganjineh\` entirely.
- **Repair**: re-copies binaries; does not touch user data.
- Publishes self-contained win-x64 (or ARM64) — no .NET runtime download at install time.
