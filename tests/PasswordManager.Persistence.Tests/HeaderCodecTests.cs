using PasswordManager.Cryptography;
using PasswordManager.Cryptography.Kdf;
using PasswordManager.Cryptography.KeyWrapping;
using PasswordManager.Cryptography.Random;
using PasswordManager.Cryptography.Secrets;
using PasswordManager.Persistence.VaultHeader;
using Xunit;

namespace PasswordManager.Persistence.Tests;

public class HeaderCodecTests
{
    private static VaultHeaderData ValidHeader()
    {
        var random = RandomGenerator.Shared;
        var header = VaultHeaderData.NewTemplate(
            random.GetBytes(CryptoFormat.VaultIdSize),
            KdfParameters.Baseline,
            random.GetBytes(CryptoFormat.SaltSize));
        using var vmk = KeyMaterial.Random(random, CryptoFormat.KeySize);
        var kek = random.GetBytes(CryptoFormat.KeySize);
        header.PasswordWrap = new KeyWrapService().Wrap(vmk, kek, header.VaultId, CryptoFormat.WrapPurposePassword, CryptoFormat.KdfIdArgon2id);
        header.RecoveryWrap = new KeyWrapService().Wrap(vmk, kek, header.VaultId, CryptoFormat.WrapPurposeRecovery, CryptoFormat.KdfIdArgon2id);
        return header;
    }

    [Fact]
    public void Serialize_Parse_RoundTrip_Preserves_All_Fields()
    {
        var header = ValidHeader();
        var bytes = HeaderCodec.Serialize(header);

        Assert.Equal(CryptoFormat.HeaderSize, bytes.Length);
        Assert.Equal("GPVLT", System.Text.Encoding.ASCII.GetString(bytes[..5]));

        var parsed = HeaderCodec.Parse(bytes);

        Assert.Equal(header.HeaderFormatVersion, parsed.HeaderFormatVersion);
        Assert.Equal(header.VaultId, parsed.VaultId);
        Assert.Equal(header.KdfId, parsed.KdfId);
        Assert.Equal(header.Kdf, parsed.Kdf);
        Assert.Equal(header.PasswordSalt, parsed.PasswordSalt);
        Assert.Equal(header.PasswordWrap.SealedKey, parsed.PasswordWrap.SealedKey);
        Assert.Equal(header.PasswordWrap.Nonce, parsed.PasswordWrap.Nonce);
        Assert.Equal(header.RecoveryWrap.SealedKey, parsed.RecoveryWrap.SealedKey);
        Assert.Equal(header.CreatedAtUnix, parsed.CreatedAtUnix);
        Assert.Equal(header.HeaderCounter, parsed.HeaderCounter);
        Assert.Equal(header.EnvelopeFormatVersion, parsed.EnvelopeFormatVersion);
    }

    [Fact]
    public void Serialize_Produces_Zeroed_Reserved_Regions()
    {
        var bytes = HeaderCodec.Serialize(ValidHeader());

        Assert.All(bytes[0x022..0x024], b => Assert.Equal(0, b));
        Assert.All(bytes[0x032..0x034], b => Assert.Equal(0, b));
        Assert.All(bytes[0x0D2..CryptoFormat.HeaderMacOffset], b => Assert.Equal(0, b));
        Assert.All(bytes[0x120..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Header_Mac_Compute_And_Verify_RoundTrip()
    {
        var header = ValidHeader();
        var bytes = HeaderCodec.Serialize(header);
        var macKey = RandomGenerator.Shared.GetBytes(CryptoFormat.KeySize);

        var mac = HeaderCodec.ComputeHeaderMac(macKey, bytes);
        mac.CopyTo(bytes, HeaderCodec.OffsetHeaderMac);

        HeaderCodec.VerifyHeaderMac(macKey, bytes);

        bytes[0x020] ^= 0x01; // tamper inside the authenticated span must break the MAC
        Assert.Throws<PasswordManager.Cryptography.Errors.CryptoAuthenticationException>(
            () => HeaderCodec.VerifyHeaderMac(macKey, bytes));
    }
}
