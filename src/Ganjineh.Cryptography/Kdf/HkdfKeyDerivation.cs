using System.Security.Cryptography;
using System.Text;

namespace Ganjineh.Cryptography.Kdf;

/// <summary>
/// Purpose-bound key derivation from the Vault Master Key (crypto-specification.md §3).
/// info = ASCII("ganjineh.v1") ‖ 0x00 ‖ ASCII(label); salt = VaultID in every derivation, so keys
/// are cryptographically bound to their vault and cannot collide across vaults or purposes.
/// </summary>
public static class HkdfKeyDerivation
{
    public static byte[] DeriveSubkey(
        ReadOnlySpan<byte> inputKeyingMaterial,
        ReadOnlySpan<byte> vaultId,
        string purposeLabel,
        int outputLength = CryptoFormat.KeySize)
    {
        ArgumentException.ThrowIfNullOrEmpty(purposeLabel);
        if (vaultId.Length != CryptoFormat.VaultIdSize)
        {
            throw new ArgumentException("Vault ID must be 128 bits.", nameof(vaultId));
        }

        var info = BuildInfo(purposeLabel);
        var output = new byte[outputLength];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, inputKeyingMaterial, output, vaultId, info);
        return output;
    }

    /// <summary>PEK from the Argon2id output (the raw KDF output is only ever an IKM here).</summary>
    public static byte[] DerivePasswordKek(ReadOnlySpan<byte> argon2Output, ReadOnlySpan<byte> vaultId) =>
        DeriveSubkey(argon2Output, vaultId, CryptoFormat.LabelPasswordKek);

    /// <summary>RKEK from the raw 256-bit recovery secret (full entropy ⇒ plain HKDF is sufficient).</summary>
    public static byte[] DeriveRecoveryKek(ReadOnlySpan<byte> recoverySecret, ReadOnlySpan<byte> vaultId) =>
        DeriveSubkey(recoverySecret, vaultId, CryptoFormat.LabelRecoveryKek);

    internal static byte[] BuildInfo(string purposeLabel)
    {
        var labelBytes = Encoding.ASCII.GetBytes(purposeLabel);
        var info = new byte[Encoding.ASCII.GetByteCount(CryptoFormat.DomainPrefix) + 1 + labelBytes.Length];
        var written = Encoding.ASCII.GetBytes(CryptoFormat.DomainPrefix, info);
        info[written++] = 0x00;
        labelBytes.CopyTo(info, written);
        return info;
    }
}
