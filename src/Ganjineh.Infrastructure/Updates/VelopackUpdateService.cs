using Ganjineh.Application.Abstractions;
using Velopack;

namespace Ganjineh.Infrastructure.Updates;

/// <summary>
/// Velopack-backed <see cref="IAppUpdateService"/> reading the public GitHub Releases feed.
/// The feed is public and unauthenticated; Velopack verifies downloaded package integrity
/// against the feed metadata (SHA-256). Publisher authenticity via Authenticode signing is
/// prepared in the pipeline (vpk --signTemplate) but NOT yet configured, so the UI never
/// downloads or applies updates without an explicit user action.
/// </summary>
public sealed class VelopackUpdateService : IAppUpdateService
{
    /// <summary>The single distribution repository (docs/deployment — never create another).</summary>
    public const string RepoUrl = "https://github.com/Reza-Noei/Ganjineh";

    private UpdateManager? _manager;
    private UpdateInfo? _pending;

    private UpdateManager Manager => _manager ??= new UpdateManager(RepoUrl);

    public string UpdateSourceUrl => RepoUrl;

    public bool CanUpdate
    {
        get
        {
            try
            {
                return Manager.IsInstalled && !Manager.IsPortable;
            }
            catch
            {
                return false;
            }
        }
    }

    public string CurrentVersion
    {
        get
        {
            try
            {
                return Manager.CurrentVersion?.ToString() ?? "?";
            }
            catch
            {
                return "?";
            }
        }
    }

    public async Task<AppUpdateCheck> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUpdate)
        {
            return new AppUpdateCheck(Succeeded: true, UpdateAvailable: false, Version: CurrentVersion, Notes: null, Error: null);
        }

        try
        {
            var info = await Manager.CheckForUpdatesAsync().WaitAsync(cancellationToken);
            if (info is null)
            {
                return new AppUpdateCheck(true, false, CurrentVersion, null, null);
            }

            _pending = info;
            return new AppUpdateCheck(
                true,
                true,
                info.TargetFullRelease.Version.ToString(),
                info.TargetFullRelease.NotesMarkdown,
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Network offline, DNS failure, proxy blocks — the UI shows an offline state and
            // the user can always reach the release page manually.
            return new AppUpdateCheck(false, false, CurrentVersion, null, ex.Message);
        }
    }

    public async Task DownloadUpdateAsync(Action<int> progress, CancellationToken cancellationToken = default)
    {
        if (_pending is null)
        {
            throw new InvalidOperationException("CheckForUpdateAsync must find an update before downloading.");
        }

        await Manager.DownloadUpdatesAsync(_pending, progress, cancellationToken);
    }

    public void ApplyUpdateAndRestart()
    {
        if (_pending is null)
        {
            throw new InvalidOperationException("No downloaded update to apply.");
        }

        Manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }
}
