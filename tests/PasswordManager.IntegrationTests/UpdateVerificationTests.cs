using System.Text;
using NSec.Cryptography;
using PasswordManager.Application.Security;

namespace PasswordManager.IntegrationTests;

/// <summary>
/// Phase 7: offline update authenticity. Uses RFC 8032 (Ed25519) test vectors for known-answer
/// verification, and NSec's API directly for round-trip tests.
/// </summary>
public sealed class UpdateVerificationTests
{
    // RFC 8032 §7.1 TEST 1 (Ed25519, empty message).
    private static readonly byte[] RfcPublicKey = Convert.FromHexString(
        "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
    private static readonly byte[] RfcSignature = Convert.FromHexString(
        "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e06522490155" +
        "5fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b");

    [Fact]
    public void Rfc8032_Vector_Verifies_With_Empty_Message()
    {
        var service = new UpdateVerificationService(RfcPublicKey);
        Assert.True(service.VerifyManifestSignature([], RfcSignature));
    }

    [Fact]
    public void Tampered_Message_Fails_Rfc_Verification()
    {
        var service = new UpdateVerificationService(RfcPublicKey);
        var message = new byte[] { 0x01 }; // non-empty ≠ RFC empty message
        Assert.False(service.VerifyManifestSignature(message, RfcSignature));
    }

    [Fact]
    public void Wrong_Public_Key_Fails_Verification()
    {
        var wrongKey = new byte[32];
        new Random(42).NextBytes(wrongKey);
        var service = new UpdateVerificationService(wrongKey);
        Assert.False(service.VerifyManifestSignature([], RfcSignature));
    }

    [Fact]
    public void Parse_Rejects_Malformed_Manifests()
    {
        var service = new UpdateVerificationService();

        Assert.ThrowsAny<Exception>(() => service.ParseManifest(Encoding.UTF8.GetBytes("{}")));
        Assert.ThrowsAny<Exception>(() => service.ParseManifest(Encoding.UTF8.GetBytes("[")));
        
        Assert.ThrowsAny<Exception>(() => service.ParseManifest(
            Encoding.UTF8.GetBytes("""{"Version":"","PackageSha256":"","PackageSizeBytes":-1}""")));
    }

    [Fact]
    public void Version_Compatibility_Checks()
    {
        var service = new UpdateVerificationService();

        Assert.True(service.IsVersionCompatible(new UpdateManifest { Version = "1.3.0" }, new Version(1, 2, 0)));
        Assert.False(service.IsVersionCompatible(new UpdateManifest { Version = "1.1.0" }, new Version(1, 2, 0)));
        Assert.False(service.IsVersionCompatible(new UpdateManifest { Version = "not-a-version" }, new Version(1, 0)));
        Assert.True(service.IsVersionCompatible(
            new UpdateManifest { Version = "1.3.0", MinimumSupportedVersion = "1.2.0" }, new Version(1, 2, 5)));
        Assert.False(service.IsVersionCompatible(
            new UpdateManifest { Version = "1.3.0", MinimumSupportedVersion = "1.2.0" }, new Version(1, 1, 0)));
    }

    /// <summary>Round-trip: NSec-generated signature verifies with NSec.Verify.</summary>
    [Fact]
    public void Nsec_Sign_Then_Nsec_Verify_RoundTrip()
    {
        var algorithm = SignatureAlgorithm.Ed25519;
        using var signingKey = Key.Create(algorithm);
        var publicKey = signingKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        var service = new UpdateVerificationService(publicKey);

        var message = Encoding.UTF8.GetBytes("round-trip");
        var signature = algorithm.Sign(signingKey, message);

        Assert.True(service.VerifyManifestSignature(message, signature));
    }
}
