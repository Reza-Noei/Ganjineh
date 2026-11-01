using PasswordManager.Cryptography.Errors;
using PasswordManager.Cryptography.KeyWrapping;
using PasswordManager.Cryptography.Secrets;
using Xunit;

namespace PasswordManager.Cryptography.Tests;

public class KeyWrapTests
{
    private readonly KeyWrapService _wrap = new();

    private static (byte[] Kek, byte[] Vmk, byte[] VaultId) Material(int seed = 1) =>
    (
        Enumerable.Repeat((byte)seed, 32).ToArray(),
        Enumerable.Repeat((byte)(seed + 100), 32).ToArray(),
        Enumerable.Repeat((byte)(seed + 200), 16).ToArray()
    );

    [Fact]
    public void Wrap_Unwrap_RoundTrip()
    {
        var (kek, vmk, vaultId) = Material();
        using var key = KeyMaterial.FromBytes(vmk);

        var record = _wrap.Wrap(key, kek, vaultId, CryptoFormat.WrapPurposePassword, CryptoFormat.KdfIdArgon2id);
        var unwrapped = _wrap.Unwrap(record, kek, vaultId, CryptoFormat.WrapPurposePassword, CryptoFormat.KdfIdArgon2id);

        Assert.Equal(vmk, unwrapped);
        Assert.Equal(CryptoFormat.KeySize, unwrapped.Length);
    }

    [Fact]
    public void Wrong_Kek_Fails_With_Generic_Authentication_Error()
    {
        var (kek, vmk, vaultId) = Material();
        var (wrongKek, _, _) = Material(seed: 2);
        using var key = KeyMaterial.FromBytes(vmk);

        var record = _wrap.Wrap(key, kek, vaultId, CryptoFormat.WrapPurposePassword, CryptoFormat.KdfIdArgon2id);

        Assert.Throws<CryptoAuthenticationException>(() =>
            _wrap.Unwrap(record, wrongKek, vaultId, CryptoFormat.WrapPurposePassword, CryptoFormat.KdfIdArgon2id));
    }

    [Fact]
    public void Wrong_Vault_Binding_Fails()
    {
        var (kek, vmk, vaultId) = Material();
        var otherVault = Enumerable.Repeat((byte)9, 16).ToArray();
        using var key = KeyMaterial.FromBytes(vmk);

        var record = _wrap.Wrap(key, kek, vaultId, CryptoFormat.WrapPurposeRecovery, CryptoFormat.KdfIdArgon2id);

        // Cross-vault substitution must fail; purpose swap must fail too.
        Assert.Throws<CryptoAuthenticationException>(() =>
            _wrap.Unwrap(record, kek, otherVault, CryptoFormat.WrapPurposeRecovery, CryptoFormat.KdfIdArgon2id));
        Assert.Throws<CryptoAuthenticationException>(() =>
            _wrap.Unwrap(record, kek, vaultId, CryptoFormat.WrapPurposePassword, CryptoFormat.KdfIdArgon2id));
    }

    [Fact]
    public void Tampered_Record_Fails()
    {
        var (kek, vmk, vaultId) = Material();
        using var key = KeyMaterial.FromBytes(vmk);

        var record = _wrap.Wrap(key, kek, vaultId, CryptoFormat.WrapPurposePassword, CryptoFormat.KdfIdArgon2id);
        var bytes = new byte[record.SerializedLength];
        record.Serialize(bytes);
        var tampered = KeyWrapRecord.Deserialize(bytes);
        tampered.SealedKey[0] ^= 0xFF;

        Assert.Throws<CryptoAuthenticationException>(() =>
            _wrap.Unwrap(tampered, kek, vaultId, CryptoFormat.WrapPurposePassword, CryptoFormat.KdfIdArgon2id));
    }

    [Fact]
    public void Serialize_Deserialize_RoundTrip()
    {
        var (_, vmk, vaultId) = Material();
        using var key = KeyMaterial.FromBytes(vmk);
        var kek = Enumerable.Repeat((byte)7, 32).ToArray();

        var record = _wrap.Wrap(key, kek, vaultId, CryptoFormat.WrapPurposePassword, CryptoFormat.KdfIdArgon2id);
        var bytes = new byte[record.SerializedLength];
        record.Serialize(bytes);

        var parsed = KeyWrapRecord.Deserialize(bytes);
        Assert.Equal(vmk, _wrap.Unwrap(parsed, kek, vaultId, CryptoFormat.WrapPurposePassword, CryptoFormat.KdfIdArgon2id));

        Assert.Throws<CryptoFormatException>(() => KeyWrapRecord.Deserialize(bytes.AsSpan()[..59]));
    }
}
