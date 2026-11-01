using PasswordManager.Cryptography.Errors;
using PasswordManager.Cryptography.Recovery;
using Xunit;

namespace PasswordManager.Cryptography.Tests;

public class RecoveryKeyCodecTests
{
    [Fact]
    public void Crc32_Known_Value()
    {
        // Standard CRC-32 check value for "123456789".
        Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"u8));
    }

    [Fact]
    public void Encode_Produce_Prefixed_Grouped_Form()
    {
        var secret = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

        var encoded = RecoveryKeyCodec.Encode(secret);

        Assert.StartsWith("PMRK1-", encoded);
        var groups = encoded.Split('-');
        Assert.Equal("PMRK1", groups[0]);
        // 59 payload chars: fourteen full groups of four plus a final group of three.
        Assert.All(groups[1..^2], group => Assert.Equal(4, group.Length));
        Assert.InRange(groups[^1].Length, 1, 4);
    }

    [Fact]
    public void RoundTrip_With_Case_Space_And_Homoglyph_Tolerance()
    {
        var secret = Enumerable.Range(7, 32).Select(i => (byte)i).ToArray();
        var encoded = RecoveryKeyCodec.Encode(secret);

        // lowercase + spaces + homoglyphs (1↔I/L, 0↔O) must still parse.
        var mangled = encoded.ToLowerInvariant().Replace("-", " ").Replace('1', 'I').Replace('0', 'O');
        var parsed = RecoveryKeyCodec.Parse(mangled);

        Assert.Equal(secret, parsed);
    }

    [Fact]
    public void Wrong_Checksum_Is_Rejected_Before_Crypto_Work()
    {
        var secret = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var encoded = RecoveryKeyCodec.Encode(secret);

        // Single-character typo in the secret region (A→B, guaranteed different symbols).
        var chars = encoded.ToCharArray();
        var editIndex = Array.FindIndex(chars, c => c == 'A');
        if (editIndex < 0)
        {
            editIndex = Array.FindIndex(chars, c => c == 'B');
            chars[editIndex] = 'C';
        }
        else
        {
            chars[editIndex] = 'B';
        }

        Assert.Throws<CryptoFormatException>(() => RecoveryKeyCodec.Parse(new string(chars)));
    }

    [Fact]
    public void Bad_Prefix_Or_Length_Is_Rejected()
    {
        Assert.Throws<CryptoFormatException>(() => RecoveryKeyCodec.Parse("XRK2-AAAA-BBBB"));
        Assert.Throws<CryptoFormatException>(() => RecoveryKeyCodec.Parse("PMRK1-SHORT"));
        Assert.Throws<CryptoFormatException>(() => RecoveryKeyCodec.Parse(string.Empty));
    }

    [Fact]
    public void Encode_Rejects_Wrong_Secret_Length()
    {
        Assert.Throws<ArgumentException>(() => RecoveryKeyCodec.Encode(new byte[16]));
    }
}
