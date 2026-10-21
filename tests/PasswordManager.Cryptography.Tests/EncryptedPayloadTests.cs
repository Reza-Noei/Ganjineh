using PasswordManager.Cryptography.Aead;
using PasswordManager.Cryptography.Errors;
using Xunit;

namespace PasswordManager.Cryptography.Tests;

public class EncryptedPayloadTests
{
    private static readonly AesGcmAuthenticatedEncryption AeadField = new();

    private static byte[] ValidEnvelope()
    {
        var key = new byte[32];
        return AeadField.Encrypt("payload"u8, key, KeyDomain.Meta, "aad"u8).Serialize();
    }

    [Fact]
    public void Serialize_Parse_RoundTrip()
    {
        var bytes = ValidEnvelope();
        var parsed = EncryptedPayload.Parse(bytes);

        Assert.Equal(1, parsed.EnvelopeVersion);
        Assert.Equal(CryptoFormat.AeadIdAes256Gcm, parsed.AeadId);
        Assert.Equal(KeyDomain.Meta, parsed.KeyDomain);
        Assert.Equal(12, parsed.Nonce.Length);
        Assert.Equal(16, parsed.Tag.Length);
        Assert.Equal(7, parsed.Ciphertext.Length);
    }

    [Theory]
    [InlineData(0)]    // empty
    [InlineData(5)]    // shorter than magic
    [InlineData(10)]   // shorter than header+tag
    public void Truncated_Envelopes_Are_Rejected_Safely(int length)
    {
        var bytes = ValidEnvelope();
        Assert.Throws<CryptoFormatException>(() => EncryptedPayload.Parse(bytes.AsSpan()[..length]));
    }

    [Fact]
    public void Bad_Magic_Is_Rejected()
    {
        var bytes = ValidEnvelope();
        bytes[0] = (byte)'X';
        Assert.Throws<CryptoFormatException>(() => EncryptedPayload.Parse(bytes));
    }

    [Fact]
    public void Unsupported_Version_Is_Rejected_With_Distinct_Type()
    {
        var bytes = ValidEnvelope();
        bytes[6] = 9;
        Assert.Throws<UnsupportedFormatException>(() => EncryptedPayload.Parse(bytes));
    }

    [Fact]
    public void Unsupported_Algorithm_Is_Rejected()
    {
        var bytes = ValidEnvelope();
        var aeadId = BitConverter.GetBytes((ushort)0x00FF);
        aeadId.CopyTo(bytes.AsSpan(7));
        Assert.Throws<UnsupportedFormatException>(() => EncryptedPayload.Parse(bytes));
    }

    [Fact]
    public void Unknown_Key_Domain_Is_Rejected()
    {
        var bytes = ValidEnvelope();
        BitConverter.GetBytes((ushort)0x7777).CopyTo(bytes.AsSpan(9));
        Assert.Throws<CryptoFormatException>(() => EncryptedPayload.Parse(bytes));
    }

    [Fact]
    public void Flip_Tag_Bit_Auth_Fails_Decrypt()
    {
        var key = new byte[32];
        var payload = AeadField.Encrypt("payload"u8, key, KeyDomain.Meta, "aad"u8);
        payload.Tag[0] ^= 0x01;

        Assert.Throws<CryptoAuthenticationException>(() => AeadField.Decrypt(payload, key, "aad"u8));
    }
}

