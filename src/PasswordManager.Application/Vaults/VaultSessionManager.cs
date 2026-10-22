using System.Security.Cryptography;
using PasswordManager.Cryptography;
using PasswordManager.Cryptography.Aead;
using PasswordManager.Cryptography.Keys;
using PasswordManager.Persistence;
using PasswordManager.Persistence.Items;
using PasswordManager.Persistence.Registry;
using PasswordManager.Persistence.SqlCipher;

namespace PasswordManager.Application.Vaults;

/// <summary>
/// Owns the single active unlocked vault session plus a monotonically increasing generation.
/// Lock/switch disposes the session and bumps the generation; stale UI results from a previous
/// session are detected by comparing generations (contract §11).
/// </summary>
public interface IVaultSessionManager : IDisposable
{
    VaultSession? Current { get; }
    bool HasActiveSession { get; }
    long Generation { get; }
    event EventHandler? SessionChanged;

    void Activate(VaultSession session);
    void LockCurrent();
}

public sealed class VaultSessionManager : IVaultSessionManager
{
    private VaultSession? _current;

    public VaultSession? Current => _current;

    public bool HasActiveSession => _current is { State: VaultSessionState.Unlocked };

    public long Generation { get; private set; }

    public event EventHandler? SessionChanged;

    public void Activate(VaultSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_current is not null)
        {
            _current.Lock(); // lock when switching vaults
        }

        _current = session;
        Generation++;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void LockCurrent()
    {
        if (_current is not null)
        {
            _current.Lock();
            _current = null;
            Generation++;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose() => LockCurrent();
}

/// <summary>Thrown when a credential operation arrives for a session that is no longer active.</summary>
public sealed class StaleSessionException(string message) : InvalidOperationException(message);
