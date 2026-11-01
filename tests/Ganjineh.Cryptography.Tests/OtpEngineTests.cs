using PasswordManager.Cryptography.Otp;
using Xunit;

namespace PasswordManager.Cryptography.Tests;

/// <summary>RFC 4226 / RFC 6238 known-answer vectors.</summary>
public class OtpEngineTests
{
    // RFC 4226 Appendix D secret: ASCII "12345678901234567890".
    private static readonly byte[] RfcSecret = "12345678901234567890"u8.ToArray();
    private static readonly byte[] RfcSecretSha256 = "12345678901234567890123456789012"u8.ToArray();  // 32 B
    private static readonly byte[] RfcSecretSha512 =
        "1234567890123456789012345678901234567890123456789012345678901234"u8.ToArray();               // 64 B

    private readonly OtpEngine _engine = new();

    [Theory]
    [InlineData(59, "94287082", OtpAlgorithm.Sha1)]
    [InlineData(59, "46119246", OtpAlgorithm.Sha256)]
    [InlineData(59, "90693936", OtpAlgorithm.Sha512)]
    [InlineData(1111111109, "07081804", OtpAlgorithm.Sha1)]
    [InlineData(1111111109, "68084774", OtpAlgorithm.Sha256)]
    [InlineData(1111111109, "25091201", OtpAlgorithm.Sha512)]
    public void Totp_Rfc6238_Appendix_B_Vectors(long secondsSinceEpoch, string expected, OtpAlgorithm algorithm)
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(secondsSinceEpoch);
        var secret = algorithm switch
        {
            OtpAlgorithm.Sha256 => RfcSecretSha256,
            OtpAlgorithm.Sha512 => RfcSecretSha512,
            _ => RfcSecret,
        };
        Assert.Equal(expected,
            _engine.Totp(new TotpConfiguration(secret, algorithm, 8, 30), now).Code);
    }

    [Theory]
    [InlineData(0UL, "755224")]
    [InlineData(1UL, "287082")]
    [InlineData(2UL, "359152")]
    [InlineData(3UL, "969429")]
    [InlineData(4UL, "338314")]
    [InlineData(5UL, "254676")]
    [InlineData(6UL, "287922")]
    [InlineData(7UL, "162583")]
    [InlineData(8UL, "399871")]
    [InlineData(9UL, "520489")]
    public void Hotp_Rfc4226_Test_Vectors(ulong counter, string expected)
    {
        Assert.Equal(expected, _engine.Hotp(new HotpConfiguration(RfcSecret, OtpAlgorithm.Sha1, 6, counter)));
    }

    [Fact]
    public void Totp_Time_Boundaries()
    {
        var config = new TotpConfiguration("12345678901234567890"u8.ToArray(), OtpAlgorithm.Sha1, 8, 30);
        // Window 2 = [60, 90): the code is constant inside, rolls over exactly at the boundary.
        var start = DateTimeOffset.FromUnixTimeSeconds(60);

        Assert.Equal(_engine.Totp(config, start).Code, _engine.Totp(config, start.AddSeconds(29)).Code);
        Assert.NotEqual(_engine.Totp(config, start).Code, _engine.Totp(config, start.AddSeconds(30)).Code);
        Assert.Equal(_engine.Totp(config, start.AddSeconds(30)).Code,
            _engine.Totp(config, start.AddSeconds(59)).Code);
    }

    [Fact]
    public void SecondsRemaining_Counts_Down()
    {
        var config = new TotpConfiguration("12345678901234567890"u8.ToArray(), OtpAlgorithm.Sha1, 6, 30);
        var first = _engine.Totp(config, DateTimeOffset.FromUnixTimeSeconds(60));
        Assert.Equal(30, first.SecondsRemaining);
        var second = _engine.Totp(config, DateTimeOffset.FromUnixTimeSeconds(75));
        Assert.Equal(15, second.SecondsRemaining);
    }

    [Fact]
    public void Base32_Rfc4648_Vectors()
    {
        // Unpadded canonical forms (RFC 4648 §10 test vectors are padded; these are their
        // stripped equivalents, which is what OTP provisioning uses).
        Assert.Equal("", Base32.Encode([]));
        Assert.Equal("MY", Base32.Encode("f"u8));
        Assert.Equal("MZXQ", Base32.Encode("fo"u8));
        Assert.Equal("MZXW6", Base32.Encode("foo"u8));
        Assert.Equal("MZXW6YQ", Base32.Encode("foob"u8));
        Assert.Equal("MZXW6YTB", Base32.Encode("fooba"u8));
        Assert.Equal("MZXW6YTBOI", Base32.Encode("foobar"u8));
        Assert.Equal("foobar"u8.ToArray(), Base32.Decode("MZXW6YTBOI"));
    }

    [Fact]
    public void Base32_Decode_Rejects_Invalid_Characters()
    {
        Assert.Throws<FormatException>(() => Base32.Decode("ABC1")); // '1' not in alphabet
    }

    [Fact]
    public void Invalid_Secrets_And_Digits_Are_Rejected()
    {
        Assert.Throws<ArgumentException>(() => _engine.Hotp(
            new HotpConfiguration([1, 2, 3], OtpAlgorithm.Sha1, 6, 0))); // secret < 8 bytes
        Assert.Throws<ArgumentOutOfRangeException>(() => _engine.Hotp(
            new HotpConfiguration(RfcSecret, OtpAlgorithm.Sha1, 5, 0))); // digits < 6
        Assert.Throws<ArgumentOutOfRangeException>(() => _engine.Totp(
            new TotpConfiguration(RfcSecret, OtpAlgorithm.Sha1, 6, 0), // period < 1
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Clock_Drift_Is_Visible_As_SecondsRemaining()
    {
        var config = new TotpConfiguration("12345678901234567890"u8.ToArray(), OtpAlgorithm.Sha1, 6, 30);
        var driftedClock = DateTimeOffset.FromUnixTimeSeconds(59 + 46); // mid-window after drift
        Assert.Equal(15, _engine.Totp(config, driftedClock).SecondsRemaining);
    }
}
