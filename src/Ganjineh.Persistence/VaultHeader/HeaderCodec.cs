using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using PasswordManager.Cryptography;
using PasswordManager.Cryptography.Errors;
using PasswordManager.Cryptography.Kdf;
using PasswordManager.Cryptography.KeyWrapping;

namespace PasswordManager.Persistence.VaultHeader;

/// <summary>
/// Parsed view of the 512-byte bootstrap header (vault-file-format.md Â§2, FORMAT_VERSION 1).
/// Contains only unlock-critical, non-sensitive metadata â€” the vault name and all content live
/// encrypted inside the database.
/// </summary>
public sealed record VaultHeaderData
{
    public required ushort HeaderFormatVersion { get; init; }
    public required ushort MinReaderVersion { get; init; }
    public required byte[] VaultId { get; init; }              // 16
    public required ushort KdfId { get; init; }
    public required KdfParameters Kdf { get; init; }
    public required byte[] PasswordSalt { get; init; }         // 16
    public KeyWrapRecord PasswordWrap { get; set; } = null!;   // filled after key derivation
    public KeyWrapRecord RecoveryWrap { get; set; } = null!;   // filled after key derivation
    public required long CreatedAtUnix { get; init; }
    public required ulong HeaderCounter { get; set; }
    public required ushort EnvelopeFormatVersion { get; init; }
    public byte[] HeaderMac { get; set; } = new byte[CryptoFormat.KeySize]; // 32, offset 0x100

    public static VaultHeaderData NewTemplate(byte[] vaultId, KdfParameters kdf, byte[] salt) => new()
    {
        HeaderFormatVersion = CryptoFormat.HeaderFormatVersion,
        MinReaderVersion = CryptoFormat.MinReaderVersion,
        VaultId = vaultId,
        KdfId = CryptoFormat.KdfIdArgon2id,
        Kdf = kdf,
        PasswordSalt = salt,
        PasswordWrap = null!,   // set by caller before serialization
        RecoveryWrap = null!,
        CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        HeaderCounter = 1,
        EnvelopeFormatVersion = 1,
    };
}

/// <summary>
/// Strict codec for the fixed 512-byte header. Parse accepts nothing beyond the declared v1
/// layout: exact size, exact magic, zero reserved bytes, known algorithm ids, plausible lengths.
/// Any deviation is a typed format error â€” never a crash, never uncontrolled allocation.
/// </summary>
public static class HeaderCodec
{
    public const ushort OffsetHeaderSize = 0x008;
    public const ushort OffsetFormatVersion = 0x00A;
    public const ushort OffsetMinReaderVersion = 0x00C;
    public const ushort OffsetFlags = 0x00E;
    public const ushort OffsetVaultId = 0x010;
    public const ushort OffsetKdfId = 0x020;
    public const ushort OffsetKdfMemory = 0x024;
    public const ushort OffsetKdfPasses = 0x028;
    public const ushort OffsetKdfParallelism = 0x02C;
    public const ushort OffsetSaltLength = 0x030;
    public const ushort OffsetPasswordSalt = 0x034;
    public const ushort OffsetAeadId = 0x044;
    public const ushort OffsetHashId = 0x046;
    public const ushort OffsetPasswordWrapNonce = 0x048;
    public const ushort OffsetPasswordWrap = 0x054;
    public const ushort OffsetRecoveryWrapNonce = 0x084;
    public const ushort OffsetRecoveryWrap = 0x090;
    public const ushort OffsetCreatedAt = 0x0C0;
    public const ushort OffsetHeaderCounter = 0x0C8;
    public const ushort OffsetEnvelopeVersion = 0x0D0;
    public const ushort OffsetHeaderMac = 0x100;

    private static readonly byte[] Magic = "GPVLT\0\0\0"u8.ToArray();

    public static byte[] Serialize(VaultHeaderData header)
    {
        ArgumentNullException.ThrowIfNull(header);
        if (header.VaultId.Length != CryptoFormat.VaultIdSize)
        {
            throw new CryptoFormatException("Vault ID must be 128 bits.");
        }

        if (header.PasswordSalt.Length != CryptoFormat.SaltSize)
        {
            throw new CryptoFormatException("Password salt must be 128 bits.");
        }

        var bytes = new byte[CryptoFormat.HeaderSize];
        Magic.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(OffsetHeaderSize), CryptoFormat.HeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(OffsetFormatVersion), header.HeaderFormatVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(OffsetMinReaderVersion), header.MinReaderVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(OffsetFlags), 0);
        header.VaultId.CopyTo(bytes, OffsetVaultId);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(OffsetKdfId), header.KdfId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(OffsetKdfMemory), checked((uint)header.Kdf.MemoryKiB));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(OffsetKdfPasses), checked((uint)header.Kdf.Passes));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(OffsetKdfParallelism), checked((uint)header.Kdf.Parallelism));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(OffsetSaltLength), CryptoFormat.SaltSize);
        header.PasswordSalt.CopyTo(bytes, OffsetPasswordSalt);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(OffsetAeadId), CryptoFormat.AeadIdAes256Gcm);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(OffsetHashId), CryptoFormat.HashIdSha256);
        header.PasswordWrap.Nonce.CopyTo(bytes, OffsetPasswordWrapNonce);
        header.PasswordWrap.SealedKey.CopyTo(bytes, OffsetPasswordWrap);
        header.RecoveryWrap.Nonce.CopyTo(bytes, OffsetRecoveryWrapNonce);
        header.RecoveryWrap.SealedKey.CopyTo(bytes, OffsetRecoveryWrap);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(OffsetCreatedAt), header.CreatedAtUnix);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(OffsetHeaderCounter), header.HeaderCounter);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(OffsetEnvelopeVersion), header.EnvelopeFormatVersion);
        // 0x0D2â€“0x0FF and 0x120â€“0x1FF stay zero (reserved).
        header.HeaderMac.CopyTo(bytes, OffsetHeaderMac);
        return bytes;
    }

    public static VaultHeaderData Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != CryptoFormat.HeaderSize)
        {
            throw new CryptoFormatException($"Vault header must be exactly {CryptoFormat.HeaderSize} bytes.");
        }

        if (!bytes[..Magic.Length].SequenceEqual(Magic))
        {
            throw new CryptoFormatException("Vault header magic mismatch.");
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(bytes[OffsetHeaderSize..]) != CryptoFormat.HeaderSize)
        {
            throw new CryptoFormatException("Header size field mismatch.");
        }

        var formatVersion = BinaryPrimitives.ReadUInt16LittleEndian(bytes[OffsetFormatVersion..]);
        if (formatVersion > CryptoFormat.HeaderFormatVersion)
        {
            throw new UnsupportedFormatException(
                $"Vault format {formatVersion} is newer than this application supports.");
        }

        if (formatVersion != CryptoFormat.HeaderFormatVersion)
        {
            throw new UnsupportedFormatException($"Vault format {formatVersion} is not supported.");
        }

        var minReader = BinaryPrimitives.ReadUInt16LittleEndian(bytes[OffsetMinReaderVersion..]);
        if (minReader > CryptoFormat.MinReaderVersion)
        {
            throw new UnsupportedFormatException("This vault requires a newer application version.");
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(bytes[OffsetFlags..]) != 0
            || bytes[0x022..0x024].ContainsAnyExcept((byte)0)
            || bytes[0x032..OffsetPasswordSalt].ContainsAnyExcept((byte)0)
            || bytes[0x0D2..CryptoFormat.HeaderMacOffset].ContainsAnyExcept((byte)0)
            || bytes[0x120..].ContainsAnyExcept((byte)0))
        {
            throw new CryptoFormatException("Reserved header bytes must be zero.");
        }

        var kdfId = BinaryPrimitives.ReadUInt16LittleEndian(bytes[OffsetKdfId..]);
        if (kdfId != CryptoFormat.KdfIdArgon2id)
        {
            throw new UnsupportedFormatException($"KDF id {kdfId} is not supported.");
        }

        var aeadId = BinaryPrimitives.ReadUInt16LittleEndian(bytes[OffsetAeadId..]);
        if (aeadId != CryptoFormat.AeadIdAes256Gcm)
        {
            throw new UnsupportedFormatException($"AEAD id {aeadId} is not supported.");
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(bytes[OffsetHashId..]) != CryptoFormat.HashIdSha256)
        {
            throw new UnsupportedFormatException("Hash id is not supported.");
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(bytes[OffsetSaltLength..]) != CryptoFormat.SaltSize)
        {
            throw new CryptoFormatException("Password salt length must be 16.");
        }

        var kdf = new KdfParameters(
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[OffsetKdfMemory..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[OffsetKdfPasses..]),
            (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[OffsetKdfParallelism..]));

        return new VaultHeaderData
        {
            HeaderFormatVersion = formatVersion,
            MinReaderVersion = minReader,
            VaultId = bytes.Slice(OffsetVaultId, CryptoFormat.VaultIdSize).ToArray(),
            KdfId = kdfId,
            Kdf = kdf,
            PasswordSalt = bytes.Slice(OffsetPasswordSalt, CryptoFormat.SaltSize).ToArray(),
            PasswordWrap = KeyWrapRecord.Deserialize(
                bytes.Slice(OffsetPasswordWrapNonce, KeyWrapRecord.NonceLength),
                bytes.Slice(OffsetPasswordWrap, KeyWrapRecord.SealedKeyLength)),
            RecoveryWrap = KeyWrapRecord.Deserialize(
                bytes.Slice(OffsetRecoveryWrapNonce, KeyWrapRecord.NonceLength),
                bytes.Slice(OffsetRecoveryWrap, KeyWrapRecord.SealedKeyLength)),
            CreatedAtUnix = BinaryPrimitives.ReadInt64LittleEndian(bytes[OffsetCreatedAt..]),
            HeaderCounter = BinaryPrimitives.ReadUInt64LittleEndian(bytes[OffsetHeaderCounter..]),
            EnvelopeFormatVersion = BinaryPrimitives.ReadUInt16LittleEndian(bytes[OffsetEnvelopeVersion..]),
            HeaderMac = bytes.Slice(OffsetHeaderMac, CryptoFormat.KeySize).ToArray(),
        };
    }

    /// <summary>The pre-header-MAC span (0x000â€“0x0FF) covered by the post-unlock header HMAC.</summary>
    public static ReadOnlySpan<byte> AuthenticatedSpan(ReadOnlySpan<byte> headerBytes) =>
        headerBytes[..CryptoFormat.HeaderMacOffset];

    /// <summary>Computes the post-unlock header MAC over the authenticated span.</summary>
    public static byte[] ComputeHeaderMac(ReadOnlySpan<byte> headerMacKey, ReadOnlySpan<byte> headerBytes) =>
        HMACSHA256.HashData(headerMacKey, AuthenticatedSpan(headerBytes));

    public static void VerifyHeaderMac(ReadOnlySpan<byte> headerMacKey, ReadOnlySpan<byte> headerBytes)
    {
        var expected = ComputeHeaderMac(headerMacKey, headerBytes);
        var stored = headerBytes.Slice(CryptoFormat.HeaderMacOffset, CryptoFormat.KeySize);
        if (!CryptographicOperations.FixedTimeEquals(expected, stored))
        {
            throw new CryptoAuthenticationException();
        }
    }
}


