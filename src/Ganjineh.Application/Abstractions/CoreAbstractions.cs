using System.Globalization;

namespace Ganjineh.Application.Abstractions;

/// <summary>Clock abstraction so time-dependent services stay testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>User-adjustable application settings. Contains no secrets (ADR-006, ST-DB-4).</summary>
public sealed record AppSettings
{
    public string Language { get; init; } = "en";
    public string Theme { get; init; } = "Light";
    public bool FirstRunCompleted { get; init; }
    /// <summary>Clipboard seconds-to-live after copy (10–120; default 30 per Phase 3 contract).</summary>
    public int ClipboardTimeoutSeconds { get; init; } = 30;
    /// <summary>Idle minutes before auto-lock; 0 disables.</summary>
    public int AutoLockMinutes { get; init; } = 10;
}

/// <summary>Persists <see cref="AppSettings"/> atomically; implementations must never store secrets.</summary>
public interface IAppSettingsStore
{
    AppSettings Current { get; }
    void Update(AppSettings settings);
}

/// <summary>Filesystem layout per ADR-006: portable when a Data directory sits beside the executable.</summary>
public interface IAppPaths
{
    bool IsPortable { get; }
    string RootDir { get; }
    string SettingsFilePath { get; }
}

/// <summary>
/// Localization contract: the neutral culture is Persian (fa); English is a satellite.
/// WPF types are deliberately absent — the Desktop layer maps <see cref="IsRightToLeft"/> to FlowDirection.
/// </summary>
public interface ILocalizer : System.ComponentModel.INotifyPropertyChanged
{
    CultureInfo CurrentCulture { get; }
    bool IsRightToLeft { get; }
    string this[string key] { get; }
    void SetCulture(string name);
    event EventHandler? CultureChanged;
}

/// <summary>Outcome of an update-feed check.</summary>
/// <param name="Succeeded">False when the feed could not be reached (offline / blocked).</param>
/// <param name="UpdateAvailable">True when the feed offers a version newer than the running one.</param>
public sealed record AppUpdateCheck(
    bool Succeeded,
    bool UpdateAvailable,
    string Version,
    string? Notes,
    string? Error);

/// <summary>
/// Application self-update contract (Velopack-backed, GitHub Releases feed).
/// Update operations are USER-initiated only — Ganjineh never downloads or installs
/// silently: Velopack verifies package integrity against the release feed, but publisher
/// authenticity ultimately depends on code signing, which is not yet configured.
/// </summary>
public interface IAppUpdateService
{
    /// <summary>True only for packaged builds; unpackaged dev/test runs cannot self-update.</summary>
    bool CanUpdate { get; }
    string CurrentVersion { get; }
    /// <summary>Public GitHub Releases feed (no authentication tokens embedded).</summary>
    string UpdateSourceUrl { get; }
    Task<AppUpdateCheck> CheckForUpdateAsync(CancellationToken cancellationToken = default);
    Task DownloadUpdateAsync(Action<int> progress, CancellationToken cancellationToken = default);
    /// <summary>Applies the downloaded update and relaunches. Always user-consented.</summary>
    void ApplyUpdateAndRestart();
}
