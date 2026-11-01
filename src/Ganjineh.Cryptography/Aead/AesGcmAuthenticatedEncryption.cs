using System.Security.Cryptography;
using Ganjineh.Cryptography.Errors;
using Ganjineh.Cryptography.Random;

namespace Ganjineh.Cryptography.Aead;

/// <summary>
/// Contract suggested by the Phase 2 spec: authenticated encryption with versioned envelope output.
/// The envelope carries format version + algorithm id + nonce + ciphertext + tag; the caller binds
/// context (vault id, record id, purpose) through the associated data.
/// </summary>
public interface IAuthenticatedEncryptionService
{
    EncryptedPayload Encrypt(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> key,
        KeyDomain keyDomain,
        ReadOnlySpan<byte> associatedData);

    byte[] Decrypt(
        EncryptedPayload payload,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> associatedData);
}

/// <summary>AES-256-GCM with 96-bit random nonces and 128-bit tags (ADR-004).</summary>
public sealed class AesGcmAuthenticatedEncryption : IAuthenticatedEncryptionService
{
    private readonly IRandomGenerator _random;

    public AesGcmAuthenticatedEncryption(IRandomGenerator? random = null)
    {
        _random = random ?? RandomGenerator.Shared;
    }

    public EncryptedPayload Encrypt(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> key,
        KeyDomain keyDomain,
        ReadOnlySpan<byte> associatedData)
    {
        ValidateKey(key);
        var nonce = _random.GetBytes(CryptoFormat.NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[CryptoFormat.TagSize];

        using var aes = new AesGcm(key, CryptoFormat.TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return new EncryptedPayload(EncryptedPayload.Version, CryptoFormat.AeadIdAes256Gcm, keyDomain, nonce, ciphertext, tag);
    }

    public byte[] Decrypt(EncryptedPayload payload, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ValidateKey(key);

        if (payload.EnvelopeVersion != EncryptedPayload.Version
            || payload.AeadId != CryptoFormat.AeadIdAes256Gcm
            || payload.Nonce.Length != CryptoFormat.NonceSize
            || payload.Tag.Length != CryptoFormat.TagSize)
        {
            throw new CryptoFormatException("Envelope parameters do not match this engine.");
        }

        var plaintext = new byte[payload.Ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, CryptoFormat.TagSize);
            aes.Decrypt(payload.Nonce, payload.Ciphertext, payload.Tag, plaintext, associatedData);
            return plaintext;
        }
        catch (AuthenticationTagMismatchException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new CryptoAuthenticationException();
        }
    }

    private static void ValidateKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != CryptoFormat.KeySize)
        {
            throw new ArgumentException("Key must be 256 bits.", nameof(key));
        }
    }
}
