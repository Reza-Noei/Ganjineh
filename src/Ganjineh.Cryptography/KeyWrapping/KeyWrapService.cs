using System.Security.Cryptography;
using System.Text;
using Ganjineh.Cryptography.Aead;
using Ganjineh.Cryptography.Errors;
using Ganjineh.Cryptography.Random;
using Ganjineh.Cryptography.Secrets;

namespace Ganjineh.Cryptography.KeyWrapping;

/// <summary>An authenticated wrapping of a 256-bit key: nonce + ciphertext+tag (80 bytes on the wire).</summary>
public sealed class KeyWrapRecord
{
    public const int NonceLength = CryptoFormat.NonceSize;
    public const int SealedKeyLength = CryptoFormat.KeySize + CryptoFormat.TagSize;

    public KeyWrapRecord(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> sealedKey)
    {
        if (nonce.Length != NonceLength || sealedKey.Length != SealedKeyLength)
        {
            throw new ArgumentException("Key wrap record has invalid lengths.");
        }

        Nonce = nonce.ToArray();
        SealedKey = sealedKey.ToArray();
    }

    public byte[] Nonce { get; }
    public byte[] SealedKey { get; }

    public int SerializedLength => NonceLength + SealedKeyLength;

    public void Serialize(Span<byte> destination)
    {
        Nonce.CopyTo(destination);
        SealedKey.CopyTo(destination[NonceLength..]);
    }

    public static KeyWrapRecord Deserialize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != NonceLength + SealedKeyLength)
        {
            throw new CryptoFormatException("Key wrap record has invalid length.");
        }

        return new KeyWrapRecord(bytes[..NonceLength], bytes[NonceLength..]);
    }

    public static KeyWrapRecord Deserialize(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> sealedKey) =>
        new(nonce, sealedKey);
}

/// <summary>
/// Authenticated wrapping of the Vault Master Key under a KEK (password path or recovery path).
///
/// AAD construction (vault-file-format.md §2, Revision 1): a canonical 27-byte structure binding
/// wrap purpose, vault identity, format version and algorithm ids. Header *metadata* (KDF salt and
/// parameters) is authenticated transitively for the password path — any modification changes the
/// PEK and the unwrap fails; the full header span is authenticated post-unlock by the header MAC.
/// </summary>
public sealed class KeyWrapService
{
    private readonly IAuthenticatedEncryptionService _aead;
    private readonly IRandomGenerator _random;

    public KeyWrapService(IAuthenticatedEncryptionService? aead = null, IRandomGenerator? random = null)
    {
        _aead = aead ?? new AesGcmAuthenticatedEncryption();
        _random = random ?? RandomGenerator.Shared;
    }

    public static byte[] BuildWrapAad(ReadOnlySpan<byte> vaultId, ushort wrapPurpose, ushort kdfId)
    {
        if (vaultId.Length != CryptoFormat.VaultIdSize)
        {
            throw new ArgumentException("Vault ID must be 128 bits.", nameof(vaultId));
        }

        var aad = new byte[Encoding.ASCII.GetByteCount(CryptoFormat.WrapAadMagic)
            + 1 + CryptoFormat.VaultIdSize + 2 + 2 + 2];
        var offset = 0;
        offset += Encoding.ASCII.GetBytes(CryptoFormat.WrapAadMagic, aad);
        aad[offset++] = (byte)wrapPurpose;
        vaultId.CopyTo(aad.AsSpan(offset));
        offset += CryptoFormat.VaultIdSize;
        BitConverter.GetBytes((ushort)CryptoFormat.HeaderFormatVersion).CopyTo(aad.AsSpan(offset));
        offset += 2;
        BitConverter.GetBytes(kdfId).CopyTo(aad.AsSpan(offset));
        offset += 2;
        BitConverter.GetBytes((ushort)CryptoFormat.AeadIdAes256Gcm).CopyTo(aad.AsSpan(offset));
        return aad;
    }

    public KeyWrapRecord Wrap(KeyMaterial keyToWrap, ReadOnlySpan<byte> kek, ReadOnlySpan<byte> vaultId, ushort wrapPurpose, ushort kdfId)
    {
        var aad = BuildWrapAad(vaultId, wrapPurpose, kdfId);
        var nonce = _random.GetBytes(CryptoFormat.NonceSize);
        var ciphertext = new byte[keyToWrap.Length];
        var tag = new byte[CryptoFormat.TagSize];

        using var aes = new AesGcm(kek, CryptoFormat.TagSize);
        aes.Encrypt(nonce, keyToWrap.Span, ciphertext, tag, aad);

        var sealedKey = new byte[SealedKeyLength()];
        ciphertext.CopyTo(sealedKey, 0);
        tag.CopyTo(sealedKey, ciphertext.Length);
        return new KeyWrapRecord(nonce, sealedKey);
    }

    /// <summary>Unwraps the key material; failure is generic authentication failure.</summary>
    public byte[] Unwrap(KeyWrapRecord record, ReadOnlySpan<byte> kek, ReadOnlySpan<byte> vaultId, ushort wrapPurpose, ushort kdfId)
    {
        var aad = BuildWrapAad(vaultId, wrapPurpose, kdfId);
        var ciphertext = record.SealedKey.AsSpan()[..CryptoFormat.KeySize];
        var tag = record.SealedKey[CryptoFormat.KeySize..];

        var plaintext = new byte[CryptoFormat.KeySize];
        try
        {
            using var aes = new AesGcm(kek, CryptoFormat.TagSize);
            aes.Decrypt(record.Nonce, ciphertext, tag, plaintext, aad);
            return plaintext;
        }
        catch (AuthenticationTagMismatchException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new CryptoAuthenticationException();
        }
    }

    private static int SealedKeyLength() => CryptoFormat.KeySize + CryptoFormat.TagSize;
}
