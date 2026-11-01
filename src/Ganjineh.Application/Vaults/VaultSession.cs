using Ganjineh.Cryptography.Keys;
using Ganjineh.Persistence;
using Ganjineh.Persistence.SqlCipher;
using Ganjineh.Persistence.VaultHeader;

namespace Ganjineh.Application.Vaults;

/// <summary>Vault session states (Phase 2 contract §9).</summary>
public enum VaultSessionState
{
    Locked,
    Unlocking,
    Unlocked,
    Locking,
    Faulted,
}

/// <summary>
/// One unlocked vault session: owns the keyring, the database connection and the lifecycle state.
/// Lock/Dispose closes the database and drops all key material. Sessions are never exposed to
/// ViewModels with keys — ViewModels only ever receive decrypted display data (Phase 3+).
/// </summary>
public sealed class VaultSession : IDisposable
{
    private VaultKeyring? _keyring;
    private VaultDatabase? _database;
    private VaultSessionState _state;

    internal VaultSession(
        VaultHeaderData header,
        VaultKeyring keyring,
        VaultDatabase database,
        bool rollbackWarning)
    {
        Header = header;
        _keyring = keyring;
        _database = database;
        _vaultDirectory = System.IO.Path.GetDirectoryName(database.Path)
            ?? throw new InvalidOperationException("Vault database path has no directory component.");
        RollbackWarning = rollbackWarning;
        _state = VaultSessionState.Unlocked;
    }

    public VaultHeaderData Header { get; private set; }

    /// <summary>Vault ID as canonical UUID text (for fallback display labels).</summary>
    public string VaultIdText => new Guid(Header.VaultId).ToString();

    public VaultSessionState State => _state;

    /// <summary>True when the opened header's counter regressed against the registry high-water mark
    /// (a possible whole-file rollback). Surfaced to the user; unlock still succeeds.</summary>
    public bool RollbackWarning { get; }

    private string _vaultDirectory = string.Empty;

    /// <summary>The vault directory remains known after Lock (the UI needs it to offer re-unlock).</summary>
    public string VaultDirectory => _vaultDirectory
        ?? throw new ObjectDisposedException(nameof(VaultSession));

    internal VaultKeyring Keyring => _keyring ?? throw new ObjectDisposedException(nameof(VaultSession));

    internal VaultDatabase Database => _database ?? throw new ObjectDisposedException(nameof(VaultSession));

    internal void ReplaceHeader(VaultHeaderData header) => Header = header;

    /// <summary>Locks the vault: closes the database, zeroizes the keyring, clears references.</summary>
    public void Lock()
    {
        if (_state is not VaultSessionState.Unlocked)
        {
            throw new InvalidOperationException("Only an unlocked session can be locked.");
        }

        _state = VaultSessionState.Locking;
        try
        {
            _database?.CheckpointWal();
            _database?.Dispose();
            _keyring?.Dispose();
        }
        catch
        {
            _state = VaultSessionState.Faulted;
            _database = null;
            _keyring = null;
            throw;
        }

        _database = null;
        _keyring = null;
        _state = VaultSessionState.Locked;
    }

    /// <summary>Transition helper: marks Faulted and releases everything after a failed unlock.</summary>
    internal static void ReleasePartial(VaultKeyring? keyring, VaultDatabase? database)
    {
        database?.Dispose();
        keyring?.Dispose();
    }

    /// <summary>Idempotent teardown: Lock when unlocked; release resources in any other state.</summary>
    public void Dispose()
    {
        if (_state == VaultSessionState.Unlocked)
        {
            Lock();
            return;
        }

        _database?.Dispose();
        _keyring?.Dispose();
        _database = null;
        _keyring = null;
        if (_state == VaultSessionState.Locking)
        {
            _state = VaultSessionState.Locked;
        }
    }
}
