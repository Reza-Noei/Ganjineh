using System.Security.Cryptography;
using System.Text;
using Ganjineh.Cryptography;
using Ganjineh.Cryptography.Aead;
using Ganjineh.Cryptography.Kdf;
using Ganjineh.Cryptography.KeyWrapping;
using Ganjineh.Cryptography.Keys;
using Ganjineh.Cryptography.Random;
using Ganjineh.Cryptography.Recovery;
using Ganjineh.Cryptography.Secrets;
using Ganjineh.Persistence;
using Ganjineh.Persistence.IO;
using Ganjineh.Persistence.Registry;
using Ganjineh.Persistence.SqlCipher;
using Ganjineh.Persistence.VaultHeader;

namespace Ganjineh.Application.Vaults;

/// <summary>
/// Orchestrates the full vault lifecycle (Phase 2): creation, both unlock paths, master-password
/// change, recovery rotation and reset — with atomic header updates and counter-based rollback
/// detection. KDF and derivation work is offloaded from the caller's thread.
/// </summary>
public sealed class VaultLifecycleService : IVaultLifecycle
{
    private readonly IPasswordKdfService _kdf;
    private readonly KeyWrapService _keyWrap;
    private readonly VaultKeyDeriver _keyDeriver;
    private readonly IRandomGenerator _random;
    private readonly HeaderFileStore _headerStore;
    private readonly IAuthenticatedEncryptionService _encryption = new AesGcmAuthenticatedEncryption();
    private readonly Func<VaultRegistry> _registryFactory;

    public VaultLifecycleService(
        IPasswordKdfService? kdf = null,
        KeyWrapService? keyWrap = null,
        Func<VaultRegistry>? registryFactory = null)
    {
        _kdf = kdf ?? new Argon2idKdfService();
        _keyWrap = keyWrap ?? new KeyWrapService();
        _keyDeriver = new VaultKeyDeriver(_kdf);
        _random = RandomGenerator.Shared;
        _headerStore = new HeaderFileStore();
        _registryFactory = registryFactory ?? DefaultRegistry;
    }

    private static VaultRegistry DefaultRegistry()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ganjineh");
        return new VaultRegistry(Path.Combine(root, "vaults.json"));
    }

    public async Task<CreatedVault> CreateAsync(CreateVaultRequest request, CancellationToken cancellationToken = default)
    {
        ValidatePassword(request.MasterPassword);

        var vaultId = RandomNumberGenerator.GetBytes(CryptoFormat.VaultIdSize);
        var vaultDirectory = Path.Combine(request.Directory, VaultDirectoryName(vaultId));
        var headerPath = Path.Combine(vaultDirectory, "header.bin");
        var dbPath = Path.Combine(vaultDirectory, "vault.db");
        Directory.CreateDirectory(vaultDirectory);

        var salt = _random.GetBytes(CryptoFormat.SaltSize);
        var parameters = KdfParameters.Baseline;
        var recoverySecret = _random.GetBytes(CryptoFormat.RecoverySecretSize);
        var recoveryEncoded = RecoveryKeyCodec.Encode(recoverySecret);

        await Task.Run(() =>
        {
            var vmk = VaultKeyDeriver.NewVaultMasterKey(_random);
            try
            {
                using var vmkMaterial = KeyMaterial.FromBytes(vmk);

                var pek = _keyDeriver.DerivePasswordKek(request.MasterPassword.Span, salt, vaultId, parameters);
                var passwordWrap = _keyWrap.Wrap(vmkMaterial, pek, vaultId, CryptoFormat.WrapPurposePassword, CryptoFormat.KdfIdArgon2id);
                CryptographicOperations.ZeroMemory(pek);

                var rkek = _keyDeriver.DeriveRecoveryKek(recoverySecret, vaultId);
                var recoveryWrap = _keyWrap.Wrap(vmkMaterial, rkek, vaultId, CryptoFormat.WrapPurposeRecovery, CryptoFormat.KdfIdArgon2id);
                CryptographicOperations.ZeroMemory(rkek);

                var header = VaultHeaderData.NewTemplate(vaultId, parameters, salt);
                header.PasswordWrap = passwordWrap;
                header.RecoveryWrap = recoveryWrap;
                var bytes = HeaderCodec.Serialize(header);
                var mac = HeaderCodec.ComputeHeaderMac(HeaderMacKey(vmk, vaultId), bytes);
                mac.CopyTo(bytes, HeaderCodec.OffsetHeaderMac);
                AtomicFile.WriteAllBytes(headerPath, bytes);

                var dbKey = HkdfKeyDerivation.DeriveSubkey(vmk, vaultId, CryptoFormat.LabelSqlcipherKey);
                try
                {
                    using var bootstrapDatabase = VaultDatabase.Create(dbPath, dbKey, vaultId, header.CreatedAtUnix);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(dbKey);
                }

                _registryFactory().RecordOpen(ToIdString(vaultId), vaultDirectory, header.HeaderCounter);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(vmk);
            }
        }, cancellationToken);

        var session = await UnlockInternalAsync(headerPath, dbPath, recoverySecret, cancellationToken);
        return new CreatedVault
        {
            Session = session,
            RecoveryKey = KeyMaterial.FromBytes(recoverySecret),
            RecoveryKeyEncoded = recoveryEncoded,
        };
    }

    public async Task<VaultSession> UnlockWithPasswordAsync(string vaultDirectory, ReadOnlyMemory<char> masterPassword, CancellationToken cancellationToken = default)
    {
        var (headerPath, dbPath, headerBytes, header) = ReadVault(vaultDirectory);
        HeaderFileStore.ValidateOnOpen(header);

        return await Task.Run(() =>
        {
            VaultKeyring? keyring = null;
            VaultDatabase? database = null;
            try
            {
                var pek = _keyDeriver.DerivePasswordKek(masterPassword.Span, header.PasswordSalt, header.VaultId, header.Kdf);
                var vmk = _keyWrap.Unwrap(header.PasswordWrap, pek, header.VaultId, CryptoFormat.WrapPurposePassword, CryptoFormat.KdfIdArgon2id);
                CryptographicOperations.ZeroMemory(pek);

                keyring = FinishUnlock(headerBytes, vmk);
                CryptographicOperations.ZeroMemory(vmk);
                database = VaultDatabase.Open(dbPath, keyring.DatabaseKey, header.VaultId);

                var session = new VaultSession(header, keyring, database, RollbackDetected(header));
                RecordOpen(session);
                keyring = null;
                database = null;
                return session;
            }
            catch
            {
                VaultSession.ReleasePartial(keyring, database);
                throw;
            }
        }, cancellationToken);
    }

    public async Task<VaultSession> UnlockWithRecoveryKeyAsync(string vaultDirectory, string recoveryKey, CancellationToken cancellationToken = default)
    {
        var secret = RecoveryKeyCodec.Parse(recoveryKey);
        try
        {
            return await UnlockInternalAsync(
                Path.Combine(vaultDirectory, "header.bin"),
                Path.Combine(vaultDirectory, "vault.db"),
                secret,
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    public async Task ChangeMasterPasswordAsync(VaultSession session, ReadOnlyMemory<char> currentPassword, ReadOnlyMemory<char> newPassword, CancellationToken cancellationToken = default)
    {
        ValidatePassword(newPassword);
        var header = session.Header;
        var headerPath = Path.Combine(session.VaultDirectory, "header.bin");

        await Task.Run(() =>
        {
            // 1. Prove operator control: the current password must unwrap the current wrap.
            var currentPek = _keyDeriver.DerivePasswordKek(currentPassword.Span, header.PasswordSalt, header.VaultId, header.Kdf);
            var vmkCheck = _keyWrap.Unwrap(header.PasswordWrap, currentPek, header.VaultId, CryptoFormat.WrapPurposePassword, CryptoFormat.KdfIdArgon2id);
            CryptographicOperations.ZeroMemory(vmkCheck);
            CryptographicOperations.ZeroMemory(currentPek);

            // 2. Fresh salt, new PEK, new password wrap. VMK and every data key are untouched.
            var newSalt = _random.GetBytes(CryptoFormat.SaltSize);
            var newPek = _keyDeriver.DerivePasswordKek(newPassword.Span, newSalt, header.VaultId, header.Kdf);
            using var vmk = KeyMaterial.FromBytes(session.Keyring.VaultMasterKey);
            var newWrap = _keyWrap.Wrap(vmk, newPek, header.VaultId, CryptoFormat.WrapPurposePassword, CryptoFormat.KdfIdArgon2id);
            CryptographicOperations.ZeroMemory(newPek);

            var updated = header with { PasswordSalt = newSalt, PasswordWrap = newWrap };
            var bytes = _headerStore.WriteUpdated(headerPath, updated, session.Keyring.HeaderMacKey);
            session.ReplaceHeader(HeaderCodec.Parse(bytes));
        }, cancellationToken);
    }

    public async Task<VaultSession> ResetMasterPasswordWithRecoveryKeyAsync(string vaultDirectory, string recoveryKey, ReadOnlyMemory<char> newPassword, CancellationToken cancellationToken = default)
    {
        ValidatePassword(newPassword);
        var session = await UnlockWithRecoveryKeyAsync(vaultDirectory, recoveryKey, cancellationToken);
        try
        {
            // Same mechanics as a password change, but authorized by the recovery path: fresh salt
            // + new password wrap; the recovery wrap is preserved byte-for-byte.
            var header = session.Header;
            var newSalt = _random.GetBytes(CryptoFormat.SaltSize);
            var newPek = _keyDeriver.DerivePasswordKek(newPassword.Span, newSalt, header.VaultId, header.Kdf);
            using var vmk = KeyMaterial.FromBytes(session.Keyring.VaultMasterKey);
            var newWrap = _keyWrap.Wrap(vmk, newPek, header.VaultId, CryptoFormat.WrapPurposePassword, CryptoFormat.KdfIdArgon2id);
            CryptographicOperations.ZeroMemory(newPek);

            var updated = header with { PasswordSalt = newSalt, PasswordWrap = newWrap };
            var bytes = _headerStore.WriteUpdated(
                Path.Combine(session.VaultDirectory, "header.bin"), updated, session.Keyring.HeaderMacKey);
            session.ReplaceHeader(HeaderCodec.Parse(bytes));
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public async Task<CreatedVaultSessionRecovery> RotateRecoveryKeyAsync(VaultSession session, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var newSecret = _random.GetBytes(CryptoFormat.RecoverySecretSize);
            var encoded = RecoveryKeyCodec.Encode(newSecret);
            var rkek = _keyDeriver.DeriveRecoveryKek(newSecret, session.Header.VaultId);
            using var vmk = KeyMaterial.FromBytes(session.Keyring.VaultMasterKey);
            var newWrap = _keyWrap.Wrap(vmk, rkek, session.Header.VaultId, CryptoFormat.WrapPurposeRecovery, CryptoFormat.KdfIdArgon2id);
            CryptographicOperations.ZeroMemory(rkek);

            // The atomic header rewrite revokes the previous recovery wrap in the same write.
            var updated = session.Header with { RecoveryWrap = newWrap };
            var bytes = _headerStore.WriteUpdated(
                Path.Combine(session.VaultDirectory, "header.bin"), updated, session.Keyring.HeaderMacKey);
            session.ReplaceHeader(HeaderCodec.Parse(bytes));

            return new CreatedVaultSessionRecovery
            {
                Session = session,
                RecoveryKey = KeyMaterial.FromBytes(newSecret),
                RecoveryKeyEncoded = encoded,
            };
        }, cancellationToken);
    }

    public async Task RenameVaultAsync(VaultSession session, string newName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new PolicyViolationException("Vault name must not be empty.");
        }

        newName = newName.Trim();
        if (newName.Length > 100)
        {
            throw new PolicyViolationException("Vault name is too long (max 100 characters).");
        }

        await Task.Run(() =>
        {
            // Authoritative name: encrypted under the item key inside the vault database.
            using var nameMaterial = KeyMaterial.FromBytes(Encoding.UTF8.GetBytes(newName));
            var nameEnvelope = _encryption.Encrypt(
                nameMaterial.Span, session.Keyring.ItemKey, KeyDomain.Meta, AadForVaultName(session.Header.VaultId));
            session.Database.SetEncryptedMeta("vault_name", nameEnvelope.Serialize());

            // Plain-text convenience label (this device only, flagged in the UI).
            _registryFactory().SetLabel(ToIdString(session.Header.VaultId), newName);
        }, cancellationToken);
    }

    /// <summary>Decrypts the vault display name from an open database; null when unset.</summary>
    public string? GetVaultName(VaultSession session)
    {
        var envelopeBytes = session.Database.GetEncryptedMeta("vault_name");
        if (envelopeBytes is null)
        {
            return null;
        }

        var payload = EncryptedPayload.Parse(envelopeBytes);
        var json = _encryption.Decrypt(payload, session.Keyring.ItemKey, AadForVaultName(session.Header.VaultId));
        try
        {
            return Encoding.UTF8.GetString(json);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }

    private static byte[] AadForVaultName(ReadOnlySpan<byte> vaultId)
    {
        var recordId = Encoding.UTF8.GetBytes("vault-name");
        var aad = new byte[vaultId.Length + recordId.Length];
        vaultId.CopyTo(aad);
        recordId.CopyTo(aad, vaultId.Length);
        return aad;
    }

    // ---- internals ----

    private async Task<VaultSession> UnlockInternalAsync(string headerPath, string dbPath, byte[] recoverySecret, CancellationToken cancellationToken)
    {
        if (!File.Exists(headerPath))
        {
            throw new StorageIoException("Vault not found.");
        }

        AtomicFile.CleanupTempFiles(headerPath);
        var headerBytes = _headerStore.ReadBytes(headerPath);
        var header = HeaderCodec.Parse(headerBytes);
        HeaderFileStore.ValidateOnOpen(header);

        return await Task.Run(() =>
        {
            VaultKeyring? keyring = null;
            VaultDatabase? database = null;
            try
            {
                var rkek = _keyDeriver.DeriveRecoveryKek(recoverySecret, header.VaultId);
                var vmk = _keyWrap.Unwrap(header.RecoveryWrap, rkek, header.VaultId, CryptoFormat.WrapPurposeRecovery, CryptoFormat.KdfIdArgon2id);
                CryptographicOperations.ZeroMemory(rkek);

                keyring = FinishUnlock(headerBytes, vmk);
                CryptographicOperations.ZeroMemory(vmk);
                database = VaultDatabase.Open(dbPath, keyring.DatabaseKey, header.VaultId);

                var session = new VaultSession(header, keyring, database, RollbackDetected(header));
                RecordOpen(session);
                keyring = null;
                database = null;
                return session;
            }
            catch
            {
                VaultSession.ReleasePartial(keyring, database);
                throw;
            }
        }, cancellationToken);
    }

    /// <summary>Post-unlock integrity: full-header HMAC, then keyring derivation.</summary>
    private VaultKeyring FinishUnlock(byte[] headerBytes, byte[] vmk)
    {
        var header = HeaderCodec.Parse(headerBytes);
        HeaderCodec.VerifyHeaderMac(HeaderMacKey(vmk, header.VaultId), headerBytes);
        return _keyDeriver.DeriveKeyring(vmk, header.VaultId);
    }

    private bool RollbackDetected(VaultHeaderData header)
    {
        var highWater = _registryFactory().GetHeaderCounterHighWater(ToIdString(header.VaultId));
        return header.HeaderCounter < highWater;
    }

    private void RecordOpen(VaultSession session) =>
        _registryFactory().RecordOpen(ToIdString(session.Header.VaultId), session.VaultDirectory, session.Header.HeaderCounter);

    private static byte[] HeaderMacKey(ReadOnlySpan<byte> vmk, ReadOnlySpan<byte> vaultId) =>
        HkdfKeyDerivation.DeriveSubkey(vmk, vaultId, CryptoFormat.LabelHeaderMac);

    private static string ToIdString(byte[] vaultId) => new Guid(vaultId).ToString();

    private static string VaultDirectoryName(byte[] vaultId) => ToIdString(vaultId) + ".pvault";

    private (string HeaderPath, string DbPath, byte[] HeaderBytes, VaultHeaderData Header) ReadVault(string vaultDirectory)
    {
        var headerPath = Path.Combine(vaultDirectory, "header.bin");
        if (!File.Exists(headerPath))
        {
            throw new StorageIoException("Vault not found.");
        }

        AtomicFile.CleanupTempFiles(headerPath);
        var headerBytes = _headerStore.ReadBytes(headerPath);
        return (headerPath, Path.Combine(vaultDirectory, "vault.db"), headerBytes, HeaderCodec.Parse(headerBytes));
    }

    private static void ValidatePassword(ReadOnlyMemory<char> password)
    {
        if (password.IsEmpty || password.Span.IndexOfAnyExcept('\0') < 0)
        {
            throw new PolicyViolationException("Master password must not be empty.");
        }
    }
}

/// <summary>Request violates vault policy (empty password). Mapped to ErrorCode.PolicyViolation.</summary>
public sealed class PolicyViolationException(string message) : Exception(message);
