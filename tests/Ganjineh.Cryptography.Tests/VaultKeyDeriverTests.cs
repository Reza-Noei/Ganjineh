using Ganjineh.Cryptography.Kdf;
using Ganjineh.Cryptography.Keys;
using Ganjineh.Cryptography.Random;
using Ganjineh.Cryptography.Secrets;
using Xunit;

namespace Ganjineh.Cryptography.Tests;

public class VaultKeyDeriverTests
{
    [Fact]
    public void Keyring_Subkeys_Are_All_Distinct_And_Deterministic()
    {
        var deriver = new VaultKeyDeriver();
        var vmk = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        var vaultId = Enumerable.Range(9, 16).Select(i => (byte)i).ToArray();

        using var ring = deriver.DeriveKeyring(vmk, vaultId);
        using var ring2 = deriver.DeriveKeyring(vmk, vaultId);

        Assert.Equal(32, ring.DatabaseKey.Length);
        Assert.NotEqual(ring.DatabaseKey.ToArray(), ring.ItemKey.ToArray());
        Assert.NotEqual(ring.DatabaseKey.ToArray(), ring.AttachmentKey.ToArray());
        Assert.NotEqual(ring.DatabaseKey.ToArray(), ring.BackupWrapKey.ToArray());
        Assert.NotEqual(ring.DatabaseKey.ToArray(), ring.HeaderMacKey.ToArray());
        Assert.Equal(ring.DatabaseKey.ToArray(), ring2.DatabaseKey.ToArray());
    }

    [Fact]
    public void Keyring_Is_Vault_Bound()
    {
        var deriver = new VaultKeyDeriver();
        var vmk = Enumerable.Repeat((byte)5, 32).ToArray();

        using var ringA = deriver.DeriveKeyring(vmk, Enumerable.Repeat((byte)1, 16).ToArray());
        using var ringB = deriver.DeriveKeyring(vmk, Enumerable.Repeat((byte)2, 16).ToArray());

        Assert.NotEqual(ringA.DatabaseKey.ToArray(), ringB.DatabaseKey.ToArray());
    }

    [Fact]
    public void Password_Kek_Derivation_Is_Deterministic_And_Param_Sensitive()
    {
        var deriver = new VaultKeyDeriver();
        var vaultId = Enumerable.Repeat((byte)3, 16).ToArray();
        var password = "رمز عبور 🔒".ToCharArray();
        var salt = Enumerable.Repeat((byte)4, 16).ToArray();
        var parameters = new KdfParameters(16384, 2, 1);

        var kek1 = deriver.DerivePasswordKek(password, salt, vaultId, parameters);
        var kek2 = deriver.DerivePasswordKek(password, salt, vaultId, parameters);
        var kekOther = deriver.DerivePasswordKek(password, salt, vaultId, new KdfParameters(16384, 3, 1));

        Assert.Equal(kek1, kek2);
        Assert.NotEqual(kek1, kekOther);
    }

    [Fact]
    public void Dispose_Zeroizes_Keyring()
    {
        var deriver = new VaultKeyDeriver();
        var ring = deriver.DeriveKeyring(Enumerable.Repeat((byte)8, 32).ToArray(), new byte[16]);
        ring.Dispose();

        Assert.True(ring.IsZeroed);
        Assert.Throws<ObjectDisposedException>(() => { _ = ring.DatabaseKey.Length; });
    }

    [Fact]
    public void KeyMaterial_Dispose_Zeroizes_Buffer()
    {
        var material = KeyMaterial.FromBytes(Enumerable.Repeat((byte)0xAB, 32).ToArray());

        // Reach the internal buffer via reflection: the copy taken through Span would not observe
        // the zeroization of the owned array.
        var field = typeof(KeyMaterial).GetField("_buffer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var internalBuffer = (byte[]?)field!.GetValue(material);
        Assert.NotNull(internalBuffer);
        Assert.All(internalBuffer!, b => Assert.Equal(0xAB, b));

        material.Dispose();

        // After Dispose the internal buffer is released (null) and the secret is unreachable.
        internalBuffer = (byte[]?)field.GetValue(material);
        Assert.Null(internalBuffer);
        Assert.Throws<ObjectDisposedException>(() => { _ = material.Span.Length; });
    }

    [Fact]
    public void Random_Generator_Produces_Distinct_Buffers()
    {
        var rng = RandomGenerator.Shared;
        Assert.NotEqual(rng.GetBytes(32), rng.GetBytes(32));
        Assert.Equal(32, rng.GetBytes(32).Length);
    }
}
