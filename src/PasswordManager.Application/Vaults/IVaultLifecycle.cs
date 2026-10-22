using PasswordManager.Cryptography.Secrets;

namespace PasswordManager.Application.Vaults;

/// <summary>Request to create a new vault (Phase 2 engine API; UI wiring arrives in Phase 3).</summary>
public sealed record CreateVaultRequest
{
    public required string Directory { get; init; }
    public required ReadOnlyMemory<char> MasterPassword { get; init; }
}

/// <summary>Created vault result. The Recovery Key plaintext exists only in this object and the
/// caller MUST display it once and dispose it — it is never persisted or logged.</summary>
public sealed record CreatedVault : IDisposable
{
    public required VaultSession Session { get; init; }
    public required KeyMaterial RecoveryKey { get; init; }
    public required string RecoveryKeyEncoded { get; init; }

    public void Dispose() => RecoveryKey.Dispose();
}

public interface IVaultLifecycle
{
    Task<CreatedVault> CreateAsync(CreateVaultRequest request, CancellationToken cancellationToken = default);

    Task<VaultSession> UnlockWithPasswordAsync(string vaultDirectory, ReadOnlyMemory<char> masterPassword, CancellationToken cancellationToken = default);

    Task<VaultSession> UnlockWithRecoveryKeyAsync(string vaultDirectory, string recoveryKey, CancellationToken cancellationToken = default);

    /// <summary>Changes the master password; verifies the current one first. VMK and data unchanged.</summary>
    Task ChangeMasterPasswordAsync(VaultSession session, ReadOnlyMemory<char> currentPassword, ReadOnlyMemory<char> newPassword, CancellationToken cancellationToken = default);

    /// <summary>Resets a forgotten master password using the Recovery Key; returns an unlocked session.</summary>
    Task<VaultSession> ResetMasterPasswordWithRecoveryKeyAsync(string vaultDirectory, string recoveryKey, ReadOnlyMemory<char> newPassword, CancellationToken cancellationToken = default);

    /// <summary>Generates a new Recovery Key; the old wrap is revoked atomically. Data untouched.</summary>
    Task<CreatedVaultSessionRecovery> RotateRecoveryKeyAsync(VaultSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames the vault: writes the encrypted display name into the vault database (authoritative)
    /// and refreshes the plain-text local label in the registry. Cryptographic identity untouched.
    /// </summary>
    Task RenameVaultAsync(VaultSession session, string newName, CancellationToken cancellationToken = default);

    /// <summary>Decrypts the vault display name from the open database; null when unset.</summary>
    string? GetVaultName(VaultSession session);
}

/// <summary>Rotation result: the new Recovery Key (display once, then dispose).</summary>
public sealed record CreatedVaultSessionRecovery : IDisposable
{
    public required VaultSession Session { get; init; }
    public required KeyMaterial RecoveryKey { get; init; }
    public required string RecoveryKeyEncoded { get; init; }

    public void Dispose() => RecoveryKey.Dispose();
}
