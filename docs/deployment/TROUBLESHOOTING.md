# Troubleshooting — deployment

## CI release run fails

| Symptom | Cause | Fix |
|---|---|---|
| "Release vX already exists" guard fires | Tag/release reused | Delete the tag+release explicitly, or bump the version. Never overwrite silently. |
| Test step red | Unit/integration regressions | Fix locally, push, re-tag. |
| `vpk pack` fails with missing icon | Asset path moved | Keep `src/Ganjineh.Desktop/Assets/Icons/ganjineh_E1_1000x1000.ico`. |
| `vpk upload github` 401/403 | GITHUB_TOKEN missing `contents: write` | Keep the `permissions:` block in release.yml. |
| WiX MSI step fails | installer/Ganjineh.Installer.wxs drifted from publish layout | Compare `-d PublishDir=` usage with docs/installer-design.md. |
| Feed validation says `releases.win.json` missing | vpk upload partially failed | Re-run the failed job; vpk re-uploads idempotently (assets are content-addressed). |

## Installed app issues

| Symptom | Cause | Fix |
|---|---|---|
| App relaunches once right after install | Velopack first-run hook | Expected; no action. |
| "Could not reach the update feed" in Settings → Updates | Offline / proxy / GitHub down | Check connectivity; the release page is always reachable manually. |
| Settings → Updates shows no section | Running portable or unpackaged build (self-update unavailable by design) | Install via `Ganjineh-win-Setup.exe`. |
| Update stuck "Downloading…" | Network interruption mid-download | Click Download again; the download is retryable and hash-verified. |
| Vault missing after reinstall | Vault was never in the install dir | Check `Documents\Ganjineh\`; reinstalling never deletes it. |

## Update trust

Updates are hash-verified but the setup is unsigned until `VPK_SIGN_PARAMS` is configured
(docs/deployment/CODE_SIGNING.md). The app therefore never auto-installs updates.

## Recovering from a bad release

1. Do not delete user machines' installs — they keep working.
2. Fix forward: publish `vX.Y+1` with the fix (Velopack delivers it as an update).
3. Only in a catastrophic case (broken installer): delete the GitHub Release, fix, re-tag.
   Communicate via release notes — tags are otherwise immutable.
