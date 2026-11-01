using PasswordManager.Cryptography.Errors;

namespace PasswordManager.Cryptography.Aead;

/// <summary>AEAD algorithm identifiers on the wire (little-endian u16 in envelope headers).</summary>
public enum KeyDomain : ushort
{
    Item = 0x0001,
    Attachment = 0x0002,
    BackupInner = 0x0003,
    Meta = 0x0004,
}

/// <summary>A parsed PMENV1 envelope: version, algorithm, nonce, ciphertext, tag.</summary>
public sealed class EncryptedPayload
{
    public const ushort Version = 1;

    public EncryptedPayload(
        ushort envelopeVersion,
        ushort aeadId,
        KeyDomain keyDomain,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag)
    {
        EnvelopeVersion = envelopeVersion;
        AeadId = aeadId;
        KeyDomain = keyDomain;
        Nonce = nonce.ToArray();
        Ciphertext = ciphertext.ToArray();
        Tag = tag.ToArray();
    }

    public ushort EnvelopeVersion { get; }
    public ushort AeadId { get; }
    public KeyDomain KeyDomain { get; }
    public byte[] Nonce { get; }
    public byte[] Ciphertext { get; }
    public byte[] Tag { get; }

    /// <summary>
    /// Serializes to the PMENV1 wire format (crypto-specification.md §7):
    /// magic(6)‖version(1)‖aead_id(2 LE)‖key_domain(2 LE)‖nonce(12)‖ciphertext‖tag(16).
    /// </summary>
    public byte[] Serialize()
    {
        var buffer = new byte[CryptoFormat.EnvelopeHeaderLength + Ciphertext.Length + CryptoFormat.TagSize];
        Serialize(buffer);
        return buffer;
    }

    public void Serialize(Span<byte> destination)
    {
        var required = CryptoFormat.EnvelopeHeaderLength + Ciphertext.Length + CryptoFormat.TagSize;
        if (destination.Length < required)
        {
            throw new ArgumentException("Destination buffer too small for envelope.", nameof(destination));
        }

        "PMENV1"u8.CopyTo(destination);
        destination[6] = (byte)EnvelopeVersion;
        BitConverter.GetBytes(AeadId).CopyTo(destination[7..]);
        BitConverter.GetBytes((ushort)KeyDomain).CopyTo(destination[9..]);
        Nonce.CopyTo(destination[11..]);
        Ciphertext.CopyTo(destination[CryptoFormat.EnvelopeHeaderLength..]);
        Tag.CopyTo(destination[(CryptoFormat.EnvelopeHeaderLength + Ciphertext.Length)..]);
    }

    /// <summary>
    /// Parses with strict validation: exact magic, known version/algorithm, plausible lengths.
    /// Caps bound allocation from untrusted input (no allocation proportional to attacker-controlled
    /// lengths beyond the declared cap).
    /// </summary>
    public static EncryptedPayload Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < CryptoFormat.EnvelopeHeaderLength + CryptoFormat.TagSize)
        {
            throw new CryptoFormatException("Envelope shorter than the minimum size.");
        }

        if (!bytes[..6].SequenceEqual("PMENV1"u8))
        {
            throw new CryptoFormatException("Envelope magic mismatch.");
        }

        var version = bytes[6];
        if (version != Version)
        {
            throw new UnsupportedFormatException($"Envelope version {version} is not supported.");
        }

        var aeadId = BitConverter.ToUInt16(bytes[7..]);
        if (aeadId != CryptoFormat.AeadIdAes256Gcm)
        {
            throw new UnsupportedFormatException($"AEAD id {aeadId} is not supported.");
        }

        var keyDomain = BitConverter.ToUInt16(bytes[9..]);
        if (!Enum.IsDefined(typeof(KeyDomain), keyDomain))
        {
            throw new CryptoFormatException("Unknown key domain.");
        }

        var ciphertextLength = bytes.Length - CryptoFormat.EnvelopeHeaderLength - CryptoFormat.TagSize;
        if ((ulong)ciphertextLength > CryptoFormat.MaxEnvelopePlaintextLength)
        {
            throw new CryptoFormatException("Envelope exceeds the maximum payload length.");
        }

        return new EncryptedPayload(
            version,
            aeadId,
            (KeyDomain)keyDomain,
            bytes.Slice(11, CryptoFormat.NonceSize),
            bytes.Slice(CryptoFormat.EnvelopeHeaderLength, ciphertextLength),
            bytes.Slice(bytes.Length - CryptoFormat.TagSize, CryptoFormat.TagSize));
    }
}
