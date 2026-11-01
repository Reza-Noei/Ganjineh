using Ganjineh.Cryptography.Aead;
using Ganjineh.Cryptography.Errors;
using Ganjineh.Cryptography.Random;
using Xunit;

namespace Ganjineh.Cryptography.Tests;

/// <summary>AES-256-GCM known-answer vectors (McGrew/Viega test cases 14, 15, 18 — widely mirrored).</summary>
public class AesGcmKnownAnswerTests
{
    private static byte[] Hex(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        return bytes;
    }

    [Fact]
    public void TestCase14_EmptyPlaintext()
    {
        var key = Hex(new string('0', 64));
        var nonce = new byte[12];

        using var aes = new System.Security.Cryptography.AesGcm(key, 16);
        var ct = new byte[0];
        var tag = new byte[16];
        aes.Encrypt(nonce, ReadOnlySpan<byte>.Empty, ct, tag, ReadOnlySpan<byte>.Empty);

        Assert.Equal("530f8afbc74536b9a963b4f1c4cb738b", Convert.ToHexString(tag).ToLowerInvariant());
    }

    [Fact]
    public void TestCase15_SixteenZeroBytes()
    {
        var key = Hex(new string('0', 64));
        var nonce = new byte[12];
        var plaintext = new byte[16];

        using var aes = new System.Security.Cryptography.AesGcm(key, 16);
        var ct = new byte[16];
        var tag = new byte[16];
        aes.Encrypt(nonce, plaintext, ct, tag, ReadOnlySpan<byte>.Empty);

        Assert.Equal("cea7403d4d606b6e074ec5d3baf39d18", Convert.ToHexString(ct).ToLowerInvariant());
        Assert.Equal("d0d1c8a799996bf0265b98b5d48ab919", Convert.ToHexString(tag).ToLowerInvariant());
    }

    [Fact]
    public void TestCase_Aes256_WithAssociatedData()
    {
        // NIST AES-256/GCM case as published in Botan's gcm.vec (verified mirror); 56-byte
        // plaintext with associated data.
        var key = Hex("feffe9928665731c6d6a8f9467308308feffe9928665731c6d6a8f9467308308");
        var nonce = Hex("cafebabefacedbaddecaf888");
        var plaintext = Hex("d9313225f88406e5a55909c5aff5269a86a7a9531534f7da2e4c303d8a318a721c3c0c95956809532fcf0e2449a6b525b16aedf5aa0de657ba637b39");
        var aad = Hex("feedfacedeadbeeffeedfacedeadbeefabaddad2");

        using var aes = new System.Security.Cryptography.AesGcm(key, 16);
        var ct = new byte[plaintext.Length];
        var tag = new byte[16];
        aes.Encrypt(nonce, plaintext, ct, tag, aad);

        Assert.Equal("522dc1f099567d07f47f37a32a84427d643a8cdcbfe5c0c97598a2bd2555d1aa8cb08e48590dbb3da7b08b1056828838c5f61e6393ba7a0abcc9f662", Convert.ToHexString(ct).ToLowerInvariant());
        Assert.Equal("76fc6ece0f4e1768cddf8853bb2d551b", Convert.ToHexString(tag).ToLowerInvariant());
    }
}

/// <summary>Envelope-level round trip and adversarial cases through the service interface.</summary>
public class AesGcmServiceTests
{
    private readonly AesGcmAuthenticatedEncryption _aead = new(new DeterministicRandom());

    [Fact]
    public void RoundTrip_With_Aad_Succeeds()
    {
        var key = new byte[32];
        var aad = "vault-context"u8.ToArray();
        var plaintext = "رمز عبور 🔒"u8.ToArray();

        var payload = _aead.Encrypt(plaintext, key, KeyDomain.Item, aad);
        var decrypted = _aead.Decrypt(payload, key, aad);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Wrong_Aad_Fails_Authentication()
    {
        var key = new byte[32];
        var payload = _aead.Encrypt("secret"u8, key, KeyDomain.Item, "aad-A"u8);
        Assert.Throws<CryptoAuthenticationException>(() => _aead.Decrypt(payload, key, "aad-B"u8));
    }

    [Fact]
    public void Wrong_Key_Fails_Authentication()
    {
        var correctKey = new byte[32];
        var wrongKey = Enumerable.Repeat((byte)0xFF, 32).ToArray();
        var payload = _aead.Encrypt("secret"u8, correctKey, KeyDomain.Item, "aad"u8);
        Assert.Throws<CryptoAuthenticationException>(() => _aead.Decrypt(payload, wrongKey, "aad"u8));
    }

    [Fact]
    public void Tampered_Ciphertext_Or_Tag_Fails()
    {
        var key = new byte[32];
        var payload = _aead.Encrypt("secret"u8, key, KeyDomain.Item, "aad"u8);

        payload.Ciphertext[0] ^= 0x01;
        Assert.Throws<CryptoAuthenticationException>(() => _aead.Decrypt(payload, key, "aad"u8));

        var fresh = _aead.Encrypt("secret"u8, key, KeyDomain.Item, "aad"u8);
        fresh.Tag[^1] ^= 0x80;
        Assert.Throws<CryptoAuthenticationException>(() => _aead.Decrypt(fresh, key, "aad"u8));
    }

    [Fact]
    public void Invalid_Key_Length_Is_Rejected()
    {
        Assert.Throws<ArgumentException>(() => _aead.Encrypt("x"u8, new byte[16], KeyDomain.Item, "a"u8));
    }

    [Fact]
    public void Nonces_Are_Random_Per_Encryption()
    {
        var key = new byte[32];
        var a = _aead.Encrypt("same"u8, key, KeyDomain.Item, "a"u8);
        var b = _aead.Encrypt("same"u8, key, KeyDomain.Item, "a"u8);
        Assert.NotEqual(a.Nonce, b.Nonce);
        Assert.NotEqual(a.Ciphertext, b.Ciphertext);
    }

    /// <summary>Fixed-output RNG so nonce-dependent tests are deterministic.</summary>
    private sealed class DeterministicRandom : IRandomGenerator
    {
        private byte _counter;

        public void Fill(Span<byte> buffer)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] = _counter++;
            }
        }

        public byte[] GetBytes(int length)
        {
            var bytes = new byte[length];
            Fill(bytes);
            return bytes;
        }
    }
}
