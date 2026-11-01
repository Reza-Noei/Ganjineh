using System.Runtime.Versioning;
using System.Threading;

namespace PasswordManager.Application.Abstractions;

/// <summary>
/// Single-instance enforcement via a named Mutex (Phase 7 §10). Windows-only APIs; the
/// Application targets Windows desktop exclusively. The mutex is abandoned-mutex safe: after a
/// crash the OS releases it and the next instance acquires it cleanly (no stale lock file).
/// </summary>
[SupportedOSPlatform("windows")]
public interface ISingleInstanceLock : IDisposable
{
    /// <summary>True when this process is the primary instance.</summary>
    bool IsPrimary { get; }

    /// <summary>Signaled by the primary instance when the user (or a second instance) requests activation.</summary>
    event EventHandler? ActivateRequested;

    /// <summary>Signals the primary instance to show/activate its window. No-op when not primary.</summary>
    void RequestActivation();
}

[SupportedOSPlatform("windows")]
public sealed class SingleInstanceLock : ISingleInstanceLock
{
    private const string MutexName = @"Local\Ganjineh.SingleInstance";
    private const string ActivateEventName = @"Local\Ganjineh.Activate";

    private readonly Mutex? _mutex;
    private readonly EventWaitHandle? _activateEvent;
    private readonly RegisteredWaitHandle? _registration;

    public SingleInstanceLock()
    {
        IsPrimary = Mutex.TryOpenExisting(MutexName, out var existing);
        if (IsPrimary)
        {
            _mutex = existing;
            _activateEvent = EventWaitHandle.OpenExisting(ActivateEventName);
        }
        else
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
            IsPrimary = created;
            if (IsPrimary)
            {
                _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            }
            else
            {
                _mutex.Dispose();
                _mutex = null;
            }
        }

        if (IsPrimary && _activateEvent is not null)
        {
            // ThreadPool wait → primary activates its window on signal.
            var registered = ThreadPool.RegisterWaitForSingleObject(
                _activateEvent, (_, _) => ActivateRequested?.Invoke(this, EventArgs.Empty),
                null, Timeout.Infinite, executeOnlyOnce: false);
            _registration = registered;
        }
    }

    public event EventHandler? ActivateRequested;

    public bool IsPrimary { get; private set; }

    public void RequestActivation()
    {
        if (!IsPrimary && EventWaitHandle.TryOpenExisting(ActivateEventName, out var handle))
        {
            handle.Set();
            handle.Dispose();
        }
    }

    public void Dispose()
    {
        ActivateRequested = null;
        _registration?.Unregister(null);
        // Deliberately no ReleaseMutex: the OS releases the named mutex when the process exits,
        // which also makes crash recovery automatic (an abandoned mutex is simply destroyed).
        _mutex?.Dispose();
        IsPrimary = false;
    }
}
