# Velopack Integration — as built

Package: `Velopack 1.2.161` (app) + `vpk 1.2.161` (CLI). Versions must stay in lockstep.

## App-side

- `App.xaml.cs` has a custom `Main` (csproj: `StartupObject=Ganjineh.Desktop.App`,
  `App.xaml` compiled as `Page`) so that `VelopackApp.Build().Run()` executes before any WPF
  work — after an update, the process may relaunch into a hook that swaps binaries.
- `Ganjineh.Infrastructure.Updates.VelopackUpdateService` wraps `UpdateManager`:
  - Source: `https://github.com/Reza-Noei/Ganjineh` (auto-detected GitHub source, no token —
    the repo is public).
  - `CheckForUpdateAsync` → `AppUpdateCheck` (version, markdown notes, offline flag).
  - `DownloadUpdateAsync(progress)` with 0–100 progress.
  - `ApplyUpdateAndRestart()` — explicit, user-consented only.
  - `CanUpdate` is false for unpackaged dev/test runs and portable builds.
- `UpdateViewModel` (Settings → Updates) drives check/download/apply with localized status.
- Startup behavior: a **quiet availability check** only — never a download or install.

## Update UX

Startup → (if newer version exists) Settings → Updates shows "Version X is available."
→ user clicks **Download update** → progress → **Restart to install** → relaunch into the
new version. Offline or feed unreachable → "Could not reach the update feed."

## Installer (setup.exe)

- Per-user: `%LOCALAPPDATA%\Ganjineh`, no UAC.
- Shortcuts: Start Menu + Desktop (Velopack defaults).
- `--silent` switch supported; silent install does not launch the app.
- First launch after install relaunches once (Velopack first-run hook) — expected.
- Uninstall: `Update.exe --uninstall` (Add/Remove Programs entry is registered).

## Data preservation (verified by test)

- Vaults: `Documents\Ganjineh\` — outside the app dir; updates/uninstall never touch it.
- Settings: `%APPDATA%\Ganjineh\` — same.
- The Velopack dir `%LOCALAPPDATA%\Ganjineh\` contains only binaries + package cache.

## Packaging

```powershell
vpk pack --packId Ganjineh --packVersion <v> --packDir <publish> --mainExe Ganjineh.exe `
  --packAuthors "Reza Noei" --packTitle "Ganjineh" `
  --icon src\Ganjineh.Desktop\Assets\Icons\ganjineh_E1_1000x1000.ico
```

Produces: `Ganjineh-win-Setup.exe`, `Ganjineh-<v>-full.nupkg`, delta nupkgs (from the second
release onward), `Ganjineh-win-Portable.zip`, `releases.win.json` feed.

## Why not replace the WiX MSI?

The MSI remains for machine-wide installs (docs/installer-design.md). Velopack cannot update
an MSI install; its updates apply only to Velopack-installed copies. Both channels are built
per release from the same publish output.
