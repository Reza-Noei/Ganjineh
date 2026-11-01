using System.Text.Json;

namespace PasswordManager.Application.Security;

/// <summary>
/// Offline update authenticity verification (Phase 7 §15): the release manifest is signed with
/// Ed25519; the verification public key is embedded at build time. Unkeyed hashes are never
/// treated as publisher authenticity, and unsigned packages are rejected. No download logic —
/// the user selects the update package file.
/// </summary>
public interface IUpdateVerificationService
{
    /// <summary>Verifies the Ed25519 signature over the manifest bytes with the embedded public key.</summary>
    bool VerifyManifestSignature(ReadOnlySpan<byte> manifestJson, ReadOnlySpan<byte> signature);

    /// <summary>Parses and validates a signed update manifest; throws FormatException on malformed input.</summary>
    UpdateManifest ParseManifest(ReadOnlySpan<byte> manifestJson);

    /// <summary>True when the manifest's minimum-supported version is satisfied by this build.</summary>
    bool IsVersionCompatible(UpdateManifest manifest, Version currentVersion);
}

public sealed record UpdateManifest
{
    public string Version { get; init; } = string.Empty;
    public string? MinimumSupportedVersion { get; init; }
    public string PackageSha256 { get; init; } = string.Empty;
    public long PackageSizeBytes { get; init; }
}

public sealed class UpdateVerificationService : IUpdateVerificationService
{
    /// <summary>
    /// Ed25519 verification public key (raw 32 bytes). Provisioned at build time via the release
    /// script (release-build.md); the signing key is held offline by the release engineer.
    /// This placeholder dev key is REPLACED at release signing and must never be trusted for
    /// production packages without matching the build manifest.
    /// </summary>
    private static readonly byte[] TrustedPublicKey =
    [
        0x50, 0x4D, 0x47, 0x5F, 0x45, 0x44, 0x35, 0x31, 0x39, 0x5F, 0x50, 0x55,
        0x42, 0x4C, 0x49, 0x43, 0x5F, 0x4B, 0x45, 0x59, 0x5F, 0x50, 0x4C, 0x41,
        0x43, 0x45, 0x48, 0x4F, 0x4C, 0x44, 0x45, 0x52,
    ];

    private readonly byte[] _trustedPublicKey;

    public UpdateVerificationService(byte[]? trustedPublicKey = null) =>
        _trustedPublicKey = trustedPublicKey ?? TrustedPublicKey;

    public bool VerifyManifestSignature(ReadOnlySpan<byte> manifestJson, ReadOnlySpan<byte> signature)
    {
        if (signature.Length != 64 || _trustedPublicKey.Length != 32)
        {
            return false;
        }

        var algorithm = NSec.Cryptography.SignatureAlgorithm.Ed25519;
        var publicKey = NSec.Cryptography.PublicKey.Import(
            algorithm, _trustedPublicKey, NSec.Cryptography.KeyBlobFormat.RawPublicKey);
        return algorithm.Verify(publicKey, manifestJson.ToArray(), signature.ToArray());
    }

    public UpdateManifest ParseManifest(ReadOnlySpan<byte> manifestJson)
    {
        var manifest = System.Text.Json.JsonSerializer.Deserialize<UpdateManifest>(manifestJson)
            ?? throw new FormatException("Update manifest is empty.");
        if (string.IsNullOrWhiteSpace(manifest.Version)
            || manifest.PackageSha256.Length != 64
            || manifest.PackageSizeBytes < 0)
        {
            throw new FormatException("Update manifest is malformed.");
        }

        return manifest;
    }

    public bool IsVersionCompatible(UpdateManifest manifest, Version currentVersion)
    {
        if (!Version.TryParse(manifest.Version, out var updateVersion))
        {
            return false;
        }

        if (updateVersion < currentVersion)
        {
            return false; // downgrade — rejected
        }

        if (manifest.MinimumSupportedVersion is { } min
            && Version.TryParse(min, out var minVersion)
            && currentVersion < minVersion)
        {
            return false; // this build is too old to accept the update directly
        }

        return true;
    }
}
