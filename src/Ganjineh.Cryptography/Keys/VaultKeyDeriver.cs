using PasswordManager.Cryptography.Kdf;
using PasswordManager.Cryptography.Random;
using PasswordManager.Cryptography.Secrets;

namespace PasswordManager.Cryptography.Keys;

/// <summary>
/// The complete per-session key set derived from the Vault Master Key (key-management.md §1).
/// Owned by the vault session; Dispose zeroizes everything. Never exposed to ViewModels.
/// </summary>
public sealed class VaultKeyring : IDisposable
{
    private KeyMaterial? _vaultMasterKey;
    private KeyMaterial? _databaseKey;
    private KeyMaterial? _itemKey;
    private KeyMaterial? _attachmentKey;
    private KeyMaterial? _backupWrapKey;
    private KeyMaterial? _headerMacKey;

    internal VaultKeyring(
        KeyMaterial vaultMasterKey,
        KeyMaterial databaseKey,
        KeyMaterial itemKey,
        KeyMaterial attachmentKey,
        KeyMaterial backupWrapKey,
        KeyMaterial headerMacKey)
    {
        _vaultMasterKey = vaultMasterKey;
        _databaseKey = databaseKey;
        _itemKey = itemKey;
        _attachmentKey = attachmentKey;
        _backupWrapKey = backupWrapKey;
        _headerMacKey = headerMacKey;
    }

    public ReadOnlySpan<byte> VaultMasterKey => Owned(_vaultMasterKey);
    public ReadOnlySpan<byte> DatabaseKey => Owned(_databaseKey);
    public ReadOnlySpan<byte> ItemKey => Owned(_itemKey);
    public ReadOnlySpan<byte> AttachmentKey => Owned(_attachmentKey);
    public ReadOnlySpan<byte> BackupWrapKey => Owned(_backupWrapKey);
    public ReadOnlySpan<byte> HeaderMacKey => Owned(_headerMacKey);

    public bool IsZeroed => _vaultMasterKey is null;

    public void Dispose()
    {
        _vaultMasterKey?.Dispose();
        _databaseKey?.Dispose();
        _itemKey?.Dispose();
        _attachmentKey?.Dispose();
        _backupWrapKey?.Dispose();
        _headerMacKey?.Dispose();
        _vaultMasterKey = null;
        _databaseKey = null;
        _itemKey = null;
        _attachmentKey = null;
        _backupWrapKey = null;
        _headerMacKey = null;
    }

    private static ReadOnlySpan<byte> Owned(KeyMaterial? key)
    {
        if (key is null)
        {
            throw new ObjectDisposedException(nameof(VaultKeyring));
        }

        return key.Span;
    }
}

/// <summary>Determines every key in the hierarchy (key-management.md §5).</summary>
public sealed class VaultKeyDeriver
{
    private readonly IPasswordKdfService _kdf;

    public VaultKeyDeriver(IPasswordKdfService? kdf = null)
    {
        _kdf = kdf ?? new Argon2idKdfService();
    }

    public static byte[] NewVaultMasterKey(IRandomGenerator rng) => rng.GetBytes(CryptoFormat.KeySize);

    public static byte[] NewRecoverySecret(IRandomGenerator rng) => rng.GetBytes(CryptoFormat.RecoverySecretSize);

    public byte[] DerivePasswordKek(ReadOnlySpan<char> passwordChars, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> vaultId, KdfParameters parameters)
    {
        using var passwordBytes = KeyMaterial.FromPasswordChars(passwordChars);
        using var argon2 = KeyMaterial.FromBytes(_kdf.Derive(passwordBytes.Span, salt, parameters));
        return HkdfKeyDerivation.DerivePasswordKek(argon2.Span, vaultId);
    }

    public byte[] DeriveRecoveryKek(ReadOnlySpan<byte> recoverySecret, ReadOnlySpan<byte> vaultId) =>
        HkdfKeyDerivation.DeriveRecoveryKek(recoverySecret, vaultId);

    public VaultKeyring DeriveKeyring(ReadOnlySpan<byte> vaultMasterKey, ReadOnlySpan<byte> vaultId) =>
        new(
            KeyMaterial.FromBytes(vaultMasterKey),
            KeyMaterial.FromBytes(HkdfKeyDerivation.DeriveSubkey(vaultMasterKey, vaultId, CryptoFormat.LabelSqlcipherKey)),
            KeyMaterial.FromBytes(HkdfKeyDerivation.DeriveSubkey(vaultMasterKey, vaultId, CryptoFormat.LabelItemEncryption)),
            KeyMaterial.FromBytes(HkdfKeyDerivation.DeriveSubkey(vaultMasterKey, vaultId, CryptoFormat.LabelAttachmentEncryption)),
            KeyMaterial.FromBytes(HkdfKeyDerivation.DeriveSubkey(vaultMasterKey, vaultId, CryptoFormat.LabelBackupWrap)),
            KeyMaterial.FromBytes(HkdfKeyDerivation.DeriveSubkey(vaultMasterKey, vaultId, CryptoFormat.LabelHeaderMac)));
}
