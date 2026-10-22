# Offline Update Design — as built (Phase 7 foundation)

## Trust model

Update packages are signed with **Ed25519** (RFC 8032). The verification public key (32 bytes)
is embedded in the application binary at build time. The signing private key is held offline by
the release engineer — it never touches the build pipeline or CI.

## Verification flow

1. User selects an update package file (`.gvbak.update` — a `.gvbak` backup + update manifest).
2. `UpdateVerificationService.VerifyManifestSignature` checks the Ed25519 signature over the
   manifest bytes with the embedded public key.
3. `UpdateVerificationService.ParseManifest` validates required fields and value ranges.
4. `IsVersionCompatible` checks version monotonicity (no downgrades) and the
   minimum-supported-version gate.

Unsigned packages, tampered manifests, and version downgrades are rejected. A valid signature
proves publisher authenticity (the holder of the signing key) — an unkeyed SHA-256 hash does not.

## Scope

v1 provides the verification foundation only. Package download, delta patching, and automatic
installation are out of scope (Phase 8+ or user-managed).
