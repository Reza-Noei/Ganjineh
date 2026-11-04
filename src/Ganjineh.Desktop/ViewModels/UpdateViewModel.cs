using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ganjineh.Application.Abstractions;

namespace Ganjineh.Desktop.ViewModels;

/// <summary>
/// Settings → Updates: check / download / apply flow. Everything here is USER-initiated;
/// the only automatic behavior is a quiet availability check on startup (never a download).
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly IAppUpdateService _updates;
    private readonly ILocalizer _localizer;
    private AppUpdateCheck? _lastCheck;

    [ObservableProperty] private string _versionText = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _actionText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _updateReadyToApply;
    [ObservableProperty] private bool _isUpdateSectionVisible;

    public bool CanUpdate => _updates.CanUpdate;

    public UpdateViewModel(IAppUpdateService updates, ILocalizer localizer)
    {
        _updates = updates;
        _localizer = localizer;
        VersionText = _updates.CurrentVersion;
        IsUpdateSectionVisible = _updates.CanUpdate;
    }

    /// <summary>Quiet startup check — availability only, never a download.</summary>
    [RelayCommand]
    private async Task CheckOnStartupAsync()
    {
        if (!_updates.CanUpdate || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var check = await _updates.CheckForUpdateAsync();
            if (check.UpdateAvailable)
            {
                _lastCheck = check;
                StatusText = string.Format(_localizer["Update.Available"], check.Version);
            }
        }
        catch
        {
            // Startup check is best-effort; the user can always check manually in Settings.
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        UpdateReadyToApply = false;
        StatusText = _localizer["Update.Checking"];
        ActionText = string.Empty;
        try
        {
            var check = await _updates.CheckForUpdateAsync();
            _lastCheck = check;
            if (!check.Succeeded)
            {
                StatusText = _localizer["Update.Offline"];
            }
            else if (check.UpdateAvailable)
            {
                StatusText = string.Format(_localizer["Update.Available"], check.Version);
                ActionText = _localizer["Update.Download"];
            }
            else
            {
                StatusText = _localizer["Update.UpToDate"];
            }
        }
        catch
        {
            StatusText = _localizer["Update.Fail"];
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanDownload));
        }
    }

    public bool CanDownload => !IsBusy && _lastCheck is { Succeeded: true, UpdateAvailable: true };

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (IsBusy || !CanDownload)
        {
            return;
        }

        IsBusy = true;
        ActionText = string.Format(_localizer["Update.Downloading"], 0);
        try
        {
            await _updates.DownloadUpdateAsync(p =>
                ActionText = string.Format(_localizer["Update.Downloading"], p));
            UpdateReadyToApply = true;
            ActionText = _localizer["Update.RestartToApply"];
            StatusText = string.Format(_localizer["Update.Downloaded"], _lastCheck!.Version);
        }
        catch
        {
            ActionText = _localizer["Update.Fail"];
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanDownload));
        }
    }

    [RelayCommand]
    private void RestartToApply()
    {
        try
        {
            _updates.ApplyUpdateAndRestart();
        }
        catch
        {
            StatusText = _localizer["Update.Fail"];
        }
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanDownload));
}
