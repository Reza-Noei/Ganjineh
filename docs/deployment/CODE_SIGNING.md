# Code Signing — current state and enablement

## Current state: UNSIGNED

No trusted code-signing certificate is configured. Consequences:

- Windows SmartScreen will warn on first runs of the setup (no reputation yet).
- The Velopack setup still **verifies package integrity** (SHA-256 against the release feed).
- The app compensates on the update path: **no silent download or install** — updates are
  explicit user actions in Settings → Updates.

Do not trust any "verified publisher" claims until signing is enabled.

## How to enable Authenticode

1. Acquire an OV or EV code-signing certificate (e.g. via a CA or Azure Trusted Signing).
2. Add the signing parameters as the repository secret **`VPK_SIGN_PARAMS`** — the exact
   argument string you would pass to `signtool sign`, e.g.:

   ```
   /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a
   ```

   (for a PFX-based flow add `/f key.pfx /p <password>`; prefer a certificate store name
   over a PFX in CI — never commit certificates or passwords).
3. The release workflow signs the Velopack setup and the MSI automatically whenever the
   secret exists (the signing step is skipped when it doesn't).
4. Velopack's own `--signTemplate` hook can additionally sign inside `vpk pack` if you move
   signing into packaging.

## What signing buys

- SmartScreen reputation for the publisher.
- Publisher authenticity for updates: combined with Velopack's feed verification, users get
  end-to-end authenticity, which unlocks the option to enable background update downloads.
