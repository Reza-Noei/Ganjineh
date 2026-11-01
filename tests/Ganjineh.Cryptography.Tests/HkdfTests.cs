using System.Text;
using PasswordManager.Cryptography.Kdf;
using Xunit;

namespace PasswordManager.Cryptography.Tests;

/// <summary>HKDF-SHA-256 known-answer vectors from RFC 5869 Appendix A.</summary>
public class HkdfKnownAnswerTests
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
    public void Rfc5869_TestCase1()
    {
        // Primitive-level KAT. Our DeriveSubkey API fixes the salt to the 16-byte VaultID, so the
        // RFC cases (13-byte salt) run against the .NET HKDF primitive directly.
        var ikm = Hex(string.Concat(Enumerable.Repeat("0b", 22)));
        var salt = Hex("000102030405060708090a0b0c");
        var info = Hex("f0f1f2f3f4f5f6f7f8f9");

        var output = new byte[42];
        System.Security.Cryptography.HKDF.DeriveKey(
            System.Security.Cryptography.HashAlgorithmName.SHA256, ikm, output, salt, info);

        Assert.Equal(
            "3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865",
            Convert.ToHexString(output).ToLowerInvariant());
    }

    [Fact]
    public void Rfc5869_TestCase2_LongerInputs()
    {
        var ikm = Hex("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f");
        var salt = Hex("606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeaf");
        var info = Hex("b0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff");

        var output = new byte[82];
        System.Security.Cryptography.HKDF.DeriveKey(
            System.Security.Cryptography.HashAlgorithmName.SHA256, ikm, output, salt, info);

        Assert.Equal(
            "b11e398dc80327a1c8e7f78c596a49344f012eda2d4efad8a050cc4c19afa97c59045a99cac7827271cb41c65e590e09da3275600c2f09b8367793a9aca3db71cc30c58179ec3e87c14c01d5c1f3434f1d87",
            Convert.ToHexString(output).ToLowerInvariant());
    }

    [Fact]
    public void Rfc5869_TestCase3_ZeroLengthSaltAndInfo()
    {
        var ikm = Hex(string.Concat(Enumerable.Repeat("0b", 22)));

        var output = new byte[42];
        System.Security.Cryptography.HKDF.DeriveKey(
            System.Security.Cryptography.HashAlgorithmName.SHA256, ikm, output, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty);

        Assert.Equal(
            "8da4e775a563c18f715f802a063c5a31b8a11f5c5ee1879ec3454e5f3c738d2d9d201395faa4b61a96c8",
            Convert.ToHexString(output).ToLowerInvariant());
    }

    [Fact]
    public void DeriveSubkey_Matches_Primitive_With_Our_Info_Format()
    {
        var ikm = Hex(string.Concat(Enumerable.Repeat("0b", 22)));
        var vaultId = Hex("000102030405060708090a0b0c0d0e0f");

        var viaApi = HkdfKeyDerivation.DeriveSubkey(ikm, vaultId, "abc", 42);

        var direct = new byte[42];
        System.Security.Cryptography.HKDF.DeriveKey(
            System.Security.Cryptography.HashAlgorithmName.SHA256, ikm, direct, vaultId,
            System.Text.Encoding.ASCII.GetBytes("ganjineh.v1\0abc"));

        Assert.Equal(direct, viaApi);
    }
}

public class HkdfDerivationTests
{
    [Fact]
    public void Different_Labels_Yield_Different_Keys()
    {
        var ikm = new byte[32];
        var vaultId = new byte[16];

        var db = HkdfKeyDerivation.DeriveSubkey(ikm, vaultId, CryptoFormat.LabelSqlcipherKey);
        var items = HkdfKeyDerivation.DeriveSubkey(ikm, vaultId, CryptoFormat.LabelItemEncryption);
        var attachments = HkdfKeyDerivation.DeriveSubkey(ikm, vaultId, CryptoFormat.LabelAttachmentEncryption);
        var backup = HkdfKeyDerivation.DeriveSubkey(ikm, vaultId, CryptoFormat.LabelBackupWrap);

        Assert.NotEqual(db, items);
        Assert.NotEqual(db, attachments);
        Assert.NotEqual(db, backup);
        Assert.NotEqual(items, attachments);
    }

    [Fact]
    public void Different_Vaults_Yield_Different_Keys()
    {
        var ikm = new byte[32];
        var vaultA = Enumerable.Repeat((byte)1, 16).ToArray();
        var vaultB = Enumerable.Repeat((byte)2, 16).ToArray();

        Assert.NotEqual(
            HkdfKeyDerivation.DeriveSubkey(ikm, vaultA, CryptoFormat.LabelSqlcipherKey),
            HkdfKeyDerivation.DeriveSubkey(ikm, vaultB, CryptoFormat.LabelSqlcipherKey));
    }

    [Fact]
    public void Info_Encoding_Is_Prefixed_And_Delimited()
    {
        var info = HkdfKeyDerivation.BuildInfo("abc");
        var expected = System.Text.Encoding.ASCII.GetBytes("ganjineh.v1\0abc");

        Assert.Equal(expected, info);
    }
}
