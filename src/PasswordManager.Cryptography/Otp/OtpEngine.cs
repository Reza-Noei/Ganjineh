using System.Security.Cryptography;

namespace PasswordManager.Cryptography.Otp;

/// <summary>OTP algorithms (RFC 4226 §5.3 / RFC 6238).</summary>
public enum OtpAlgorithm
{
    Sha1 = 0,
    Sha256 = 1,
    Sha512 = 2,
}

/// <summary>Standard RFC 4648 Base32 (A–Z, 2–7), padding-tolerant decode.</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder((bytes.Length * 8 + 4) / 5);
        uint buffer = 0;
        var bits = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(int)((buffer >> (bits - 5)) & 0x1F)]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            sb.Append(Alphabet[(int)((buffer << (5 - bits)) & 0x1F)]);
        }

        return sb.ToString();
    }

    /// <summary>Decodes standard Base32; tolerates lowercase and '=' padding, rejects other characters.</summary>
    public static byte[] Decode(string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        var cleaned = encoded.Trim().TrimEnd('=').ToUpperInvariant();
        if (cleaned.Length == 0)
        {
            throw new FormatException("Empty Base32 value.");
        }

        var outputLength = cleaned.Length * 5 / 8;
        var bytes = new byte[outputLength];
        uint buffer = 0;
        var bits = 0;
        var position = 0;
        foreach (var ch in cleaned)
        {
            var value = Alphabet.IndexOf(ch);
            if (value < 0)
            {
                throw new FormatException("Invalid Base32 character.");
            }

            buffer = (buffer << 5) | (uint)value;
            bits += 5;
            if (bits >= 8)
            {
                bytes[position++] = (byte)((buffer >> (bits - 8)) & 0xFF);
                bits -= 8;
            }
        }

        if (position != outputLength)
        {
            throw new FormatException("Base32 length mismatch.");
        }

        return bytes;
    }
}

/// <summary>Result of an OTP computation.</summary>
public sealed record OtpResult(string Code, int SecondsRemaining);

public sealed record HotpConfiguration(byte[] Secret, OtpAlgorithm Algorithm, int Digits, ulong Counter);
public sealed record TotpConfiguration(byte[] Secret, OtpAlgorithm Algorithm, int Digits, int PeriodSeconds);

/// <summary>
/// RFC 4226 (HOTP) + RFC 6238 (TOTP) engine over BCL HMACs, with a testable clock.
/// Dynamic truncation per RFC 4226 §5.3; TOTP uses UTC Unix time divided by the period.
/// </summary>
public interface IOtpEngine
{
    string Hotp(HotpConfiguration configuration);
    OtpResult Totp(TotpConfiguration configuration, DateTimeOffset nowUtc);
}

public sealed class OtpEngine : IOtpEngine
{
    private static readonly Dictionary<OtpAlgorithm, Func<byte[], HMAC>> Hmacs = new()
    {
        [OtpAlgorithm.Sha1] = static key => new HMACSHA1(key),
        [OtpAlgorithm.Sha256] = static key => new HMACSHA256(key),
        [OtpAlgorithm.Sha512] = static key => new HMACSHA512(key),
    };

    public string Hotp(HotpConfiguration configuration)
    {
        Validate(configuration.Secret, configuration.Digits);
        var counterBytes = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(counterBytes, configuration.Counter);
        return Compute(configuration.Secret, configuration.Algorithm, counterBytes, configuration.Digits);
    }

    public OtpResult Totp(TotpConfiguration configuration, DateTimeOffset nowUtc)
    {
        Validate(configuration.Secret, configuration.Digits);
        if (configuration.PeriodSeconds is < 1 or > 3600)
        {
            throw new ArgumentOutOfRangeException(nameof(configuration), "TOTP period must be 1–3600 seconds.");
        }

        var counter = (ulong)(nowUtc.ToUnixTimeSeconds() / configuration.PeriodSeconds);
        var counterBytes = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(counterBytes, counter);
        var code = Compute(configuration.Secret, configuration.Algorithm, counterBytes, configuration.Digits);
        var secondsRemaining = (int)(configuration.PeriodSeconds - (nowUtc.ToUnixTimeSeconds() % configuration.PeriodSeconds));
        return new OtpResult(code, secondsRemaining);
    }

    private static string Compute(byte[] secret, OtpAlgorithm algorithm, byte[] message, int digits)
    {
        using var hmac = Hmacs[algorithm](secret);
        var hash = hmac.ComputeHash(message);
        var offset = hash[^1] & 0x0F;                       // dynamic truncation (RFC 4226 §5.3)
        var binary = ((hash[offset] & 0x7F) << 24)
                     | ((hash[offset + 1] & 0xFF) << 16)
                     | ((hash[offset + 2] & 0xFF) << 8)
                     | (hash[offset + 3] & 0xFF);
        return (binary % (int)Math.Pow(10, digits)).ToString(new string('0', digits));
    }

    private static void Validate(byte[] secret, int digits)
    {
        if (secret.Length < 8)
        {
            throw new ArgumentException("OTP secret must be at least 8 bytes (RFC 4226).", nameof(secret));
        }

        if (digits is < 6 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(digits), "OTP digits must be 6–8.");
        }
    }
}
