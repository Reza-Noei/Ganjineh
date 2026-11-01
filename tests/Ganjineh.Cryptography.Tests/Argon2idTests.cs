using System.Text;
using Ganjineh.Cryptography.Errors;
using Ganjineh.Cryptography.Kdf;
using Xunit;

namespace Ganjineh.Cryptography.Tests;

/// <summary>
/// Argon2id behavioral tests. The known-answer proof lives in Argon2idCrossValidationTests
/// (libsodium vs BouncyCastle, independent implementations); the phc-winner-argon2 published
/// vectors use an 8-byte salt, which libsodium's crypto_pwhash cannot accept (16 bytes enforced),
/// so they are unreachable from this stack — documented in cryptographic-test-results.md.
/// </summary>
public class Argon2idKnownAnswerTests
{
    [Fact]
    public void Unicode_Passwords_Are_Deterministic_And_Distinct()
    {
        var kdf = new Argon2idKdfService();
        var salt = Encoding.ASCII.GetBytes("0123456789abcdef");
        var parameters = new KdfParameters(MemoryKiB: 16384, Passes: 2, Parallelism: 1);

        var persian = Encoding.UTF8.GetBytes("رمزِحرlatin");
        var emoji = Encoding.UTF8.GetBytes("رمز🔒");
        var zwnj = Encoding.UTF8.GetBytes("رمز‌حر");

        var a1 = kdf.Derive(persian, salt, parameters);
        var a2 = kdf.Derive(persian, salt, parameters);
        var b = kdf.Derive(emoji, salt, parameters);
        var c = kdf.Derive(zwnj, salt, parameters);

        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b);
        Assert.NotEqual(a1, c);
        Assert.Equal(32, a1.Length);
    }

    [Fact]
    public void Different_Salt_Changes_Output()
    {
        var kdf = new Argon2idKdfService();
        var parameters = new KdfParameters(MemoryKiB: 16384, Passes: 2, Parallelism: 1);
        var password = Encoding.UTF8.GetBytes("same-password");

        Assert.NotEqual(
            kdf.Derive(password, Encoding.ASCII.GetBytes("0123456789abcdef"), parameters),
            kdf.Derive(password, Encoding.ASCII.GetBytes("fedcba9876543210"), parameters));
    }

    [Fact]
    public void Salt_Length_Is_Enforced()
    {
        var kdf = new Argon2idKdfService();
        Assert.Throws<ArgumentException>(() =>
            kdf.Derive("pw"u8, new byte[8], new KdfParameters(16384, 2, 1)));
    }
}

public class KdfParametersTests
{
    [Fact]
    public void Baseline_Passes_Validation()
    {
        KdfParameters.Baseline.Validate();
    }

    [Theory]
    [InlineData(8192, 3, 1)]     // memory below floor
    [InlineData(65536, 1, 1)]    // passes below floor
    [InlineData(65536, 3, 4)]    // parallelism unsupported by engine
    public void Invalid_Parameters_Are_Rejected(long memoryKiB, long passes, int parallelism)
    {
        Assert.Throws<InvalidKdfParametersException>(
            () => new KdfParameters(memoryKiB, passes, parallelism).Validate());
    }
}
