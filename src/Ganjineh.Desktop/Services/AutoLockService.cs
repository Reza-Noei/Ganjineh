using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Ganjineh.Application.Abstractions;
using Ganjineh.Application.Vaults;

namespace Ganjineh.Desktop.Services;

/// <summary>
/// Auto-lock: configurable idle timeout plus immediate lock on Windows session lock. Raises
/// LockRequested; AppViewModel routes to the unlock screen and clears all credential state.
/// </summary>
public sealed class AutoLockService
{
    private readonly IVaultSessionManager _sessions;
    private readonly IAppSettingsStore _settings;
    private readonly DispatcherTimer _timer;
    private DateTime _lastActivity = DateTime.UtcNow;

    public event EventHandler? LockRequested;

    public AutoLockService(IVaultSessionManager sessions, IAppSettingsStore settings)
    {
        _sessions = sessions;
        _settings = settings;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(15),
        };
        _timer.Tick += (_, _) => CheckIdle();
        _timer.Start();

        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public void NotifyActivity() => _lastActivity = DateTime.UtcNow;

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            RequestLock();
        }
    }

    private void CheckIdle()
    {
        var minutes = _settings.Current.AutoLockMinutes;
        if (minutes <= 0 || !_sessions.HasActiveSession)
        {
            return;
        }

        if (DateTime.UtcNow - _lastActivity >= TimeSpan.FromMinutes(minutes))
        {
            RequestLock();
        }
    }

    private void RequestLock()
    {
        _lastActivity = DateTime.UtcNow;
        if (_sessions.HasActiveSession)
        {
            LockRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
