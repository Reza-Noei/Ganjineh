using Ganjineh.Cryptography.Errors;

namespace Ganjineh.Cryptography.Recovery;

/// <summary>
/// Recovery Key codec (recovery-design.md §2): 32-byte secret + CRC-32(secret), each Crockford
/// Base32-encoded, joined as "PMRK1-" + 52 secret chars + 7 checksum chars, dash-grouped in fours.
/// Parsing is tolerant of case, spaces and homoglyphs (I/L→1, O→0, U→V) but rejects bad checksums
/// before any cryptographic work.
/// </summary>
public static class RecoveryKeyCodec
{
    public const string Prefix = "PMRK1";

    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // Crockford: no I, L, O, U

    /// <summary>Encodes a 32-byte recovery secret into the display form.</summary>
    public static string Encode(ReadOnlySpan<byte> secret)
    {
        if (secret.Length != CryptoFormat.RecoverySecretSize)
        {
            throw new ArgumentException("Recovery secret must be 256 bits.", nameof(secret));
        }

        var crc = Crc32.Compute(secret);
        var secretChars = Base32Encode(secret);
        var crcBytes = BitConverter.GetBytes(crc);
        var crcChars = Base32Encode(crcBytes);

        return DashGroup(Prefix + "-" + secretChars + crcChars);
    }

    /// <summary>Parses and checksum-validates a typed Recovery Key; throws before any KDF work.</summary>
    public static byte[] Parse(string typed)
    {
        ArgumentNullException.ThrowIfNull(typed);
        var flat = Normalize(typed);

        if (!flat.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new CryptoFormatException("Recovery Key must start with " + Prefix + ".");
        }

        var payload = flat[Prefix.Length..];
        if (payload.Length != 59)
        {
            throw new CryptoFormatException("Recovery Key has the wrong length.");
        }

        var secret = Base32Decode(payload[..52], padBitCount: 4);
        var crcBytes = Base32Decode(payload[52..], padBitCount: 3);
        var expectedCrc = BitConverter.ToUInt32(crcBytes);

        if (Crc32.Compute(secret) != expectedCrc)
        {
            throw new CryptoFormatException("Recovery Key checksum is invalid.");
        }

        return secret;
    }

    private static string Normalize(string typed)
    {
        var sb = new System.Text.StringBuilder(typed.Length);
        foreach (var ch in typed)
        {
            if (ch is '-' or ' ')
            {
                continue;
            }

            var upper = char.ToUpperInvariant(ch);
            sb.Append(upper switch
            {
                'I' or 'L' => '1',
                'O' => '0',
                'U' => 'V',
                _ => upper,
            });
        }

        return sb.ToString();
    }

    private static string DashGroup(string flat)
    {
        // The "PMRK1-" prefix is emitted verbatim; every following run of four characters is
        // dash-separated, so the display form reads in fixed groups.
        var sb = new System.Text.StringBuilder(flat.Length + flat.Length / 4);
        var separatorIndex = flat.IndexOf('-');
        sb.Append(flat, 0, separatorIndex + 1);
        var sinceDash = 0;
        for (var i = separatorIndex + 1; i < flat.Length; i++)
        {
            if (sinceDash == 4)
            {
                sb.Append('-');
                sinceDash = 0;
            }

            sb.Append(flat[i]);
            sinceDash++;
        }

        return sb.ToString();
    }

    private static string Base32Encode(ReadOnlySpan<byte> bytes)
    {
        var bits = new System.Text.StringBuilder((bytes.Length * 8 + 4) / 5);
        var buffer = 0u;
        var bitsInBuffer = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bitsInBuffer += 8;
            while (bitsInBuffer >= 5)
            {
                bits.Append(Alphabet[(int)((buffer >> (bitsInBuffer - 5)) & 0x1F)]);
                bitsInBuffer -= 5;
            }
        }

        if (bitsInBuffer > 0)
        {
            bits.Append(Alphabet[(int)((buffer << (5 - bitsInBuffer)) & 0x1F)]);
        }

        return bits.ToString();
    }

    private static byte[] Base32Decode(string chars, int padBitCount)
    {
        var outputLength = chars.Length * 5 / 8;
        var bytes = new byte[outputLength];
        var buffer = 0u;
        var bitsInBuffer = 0;
        var position = 0;

        foreach (var ch in chars)
        {
            var value = Alphabet.IndexOf(ch);
            if (value < 0)
            {
                throw new CryptoFormatException("Recovery Key contains an invalid character.");
            }

            buffer = (buffer << 5) | (uint)value;
            bitsInBuffer += 5;
            if (bitsInBuffer >= 8)
            {
                bytes[position++] = (byte)((buffer >> (bitsInBuffer - 8)) & 0xFF);
                bitsInBuffer -= 8;
            }
        }

        // Strictness: trailing padding bits must be zero.
        if (bitsInBuffer > 0 && (buffer & ((1u << bitsInBuffer) - 1)) != 0)
        {
            throw new CryptoFormatException("Recovery Key padding bits are malformed.");
        }

        if (position != outputLength || padBitCount != bitsInBuffer)
        {
            throw new CryptoFormatException("Recovery Key length is malformed.");
        }

        return bytes;
    }
}
