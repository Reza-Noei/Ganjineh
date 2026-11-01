using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ganjineh.Application.Abstractions;
using Ganjineh.Application.Backup;
using Ganjineh.Application.Vaults;
using Ganjineh.Desktop.Services;

namespace Ganjineh.Desktop.ViewModels;

/// <summary>
/// Backup &amp; restore overlay over <see cref="IVaultBackupService"/> (.gvbak v1). File dialogs
/// are owned by the shell (code-behind, like the attachment flows) and hand paths back into these
/// methods; the restore master password travels only through the char[] handoff, never a binding.
/// </summary>
public sealed partial class BackupViewModel : ObservableObject
{
    private readonly IVaultBackupService _backups;
    private readonly IVaultSessionManager _sessions;
    private readonly ILocalizer _localizer;
    private readonly IDialogService _dialogs;

    private string? _pendingRestorePath;
    private string? _restoreDestination;

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _restoreStage;
    [ObservableProperty] private string? _restorePathDisplay;
    [ObservableProperty] private string? _restoreDestinationDisplay;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _verifySummary;

    public event EventHandler? CreateBackupRequested;
    public event EventHandler? VerifyBackupRequested;
    public event EventHandler? PickRestoreFileRequested;

    public BackupViewModel(
        IVaultBackupService backups,
        IVaultSessionManager sessions,
        ILocalizer localizer,
        IDialogService dialogs)
    {
        _backups = backups;
        _sessions = sessions;
        _localizer = localizer;
        _dialogs = dialogs;
    }

    [RelayCommand]
    private void Open() => IsVisible = true;

    [RelayCommand]
    private void Close() => IsVisible = false;

    // ── create ──

    [RelayCommand]
    private void BeginCreateBackup() => CreateBackupRequested?.Invoke(this, EventArgs.Empty);

    public async Task CreateBackupToAsync(string destinationFilePath)
    {
        if (!_sessions.HasActiveSession)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _backups.CreateAsync(
                _sessions.Current!, new BackupRequest { DestinationFilePath = destinationFilePath });
            StatusMessage = result.Success
                ? _localizer["Status.BackupCreated"]
                : LocalizeError(result.ErrorKey);
        }
        catch (StaleSessionException)
        {
            StatusMessage = _localizer["Error.AuthenticationFailed"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── verify ──

    [RelayCommand]
    private void BeginVerifyBackup() => VerifyBackupRequested?.Invoke(this, EventArgs.Empty);

    public async Task VerifyBackupAtAsync(string backupFilePath)
    {
        IsBusy = true;
        try
        {
            var result = await _backups.VerifyAsync(
                backupFilePath, _sessions.HasActiveSession ? _sessions.Current : null,
                VerificationLevel.Cryptographic);
            if (result.Success)
            {
                VerifySummary = string.Join("   |   ",
                    _localizer["Backup.Verified"],
                    result.VaultIdText ?? "—",
                    $"{_localizer["Backup.ItemCount"]}: {result.ItemCount}",
                    $"{_localizer["Backup.AttachmentCount"]}: {result.AttachmentCount}",
                    $"{_localizer["Backup.CreatedAt"]}: {(result.BackupCreatedAt?.LocalDateTime ?? default):yyyy/MM/dd HH:mm}");
                StatusMessage = _localizer["Backup.Verified"];
            }
            else
            {
                VerifySummary = null;
                StatusMessage = LocalizeError(result.ErrorKey);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── restore ──

    [RelayCommand]
    private void BeginRestore() => PickRestoreFileRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Called by the shell after the restore file dialog; moves to the restore form.</summary>
    public void SetRestoreFile(string backupFilePath)
    {
        _pendingRestorePath = backupFilePath;
        RestorePathDisplay = backupFilePath;
        VerifySummary = null;
        RestoreStage = true;
    }

    [RelayCommand]
    private void PickRestoreDestination()
    {
        var folder = _dialogs.PickFolder(_restoreDestination);
        if (folder is not null)
        {
            _restoreDestination = folder;
            RestoreDestinationDisplay = folder;
        }
    }

    /// <summary>Called by the shell with the master password read from the PasswordBox (never
    /// stored on a property; the service contract takes it as a string).</summary>
    public async Task ConfirmRestoreAsync(string credentialPassword)
    {
        if (_pendingRestorePath is null || _restoreDestination is null)
        {
            StatusMessage = _localizer["Validation.RecoveryRequired"];
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _backups.RestoreAsync(new RestoreRequest
            {
                BackupFilePath = _pendingRestorePath,
                DestinationParentDirectory = _restoreDestination,
                CredentialPassword = credentialPassword,
            });
            if (result.Success)
            {
                StatusMessage = $"{_localizer["Status.RestoreCompleted"]} ({result.ItemCount})";
                CancelRestoreStage();
                IsVisible = false;
            }
            else
            {
                StatusMessage = LocalizeError(result.ErrorKey);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelRestoreStage()
    {
        RestoreStage = false;
        RestorePathDisplay = null;
        RestoreDestinationDisplay = null;
        _pendingRestorePath = null;
        _restoreDestination = null;
    }

    /// <summary>
    /// Service error keys arrive in mixed shapes: "Error.Storage", "Error.Storage:ExceptionType",
    /// or bare keys ("Unlock.WrongPassword", "Backup.Truncated"). Resolve through the resource
    /// table; if nothing matches, show the raw key rather than a localization placeholder.
    /// </summary>
    private string LocalizeError(string? errorKey)
    {
        if (string.IsNullOrEmpty(errorKey))
        {
            return _localizer["Error.Unexpected"];
        }

        var baseKey = errorKey.Split(':')[0];
        if (baseKey.StartsWith("Error.", StringComparison.Ordinal))
        {
            return _localizer[baseKey];
        }

        var direct = _localizer[baseKey];
        if (!direct.StartsWith('['))
        {
            return direct;
        }

        var prefixed = _localizer["Error." + baseKey];
        return prefixed.StartsWith('[') ? baseKey : prefixed;
    }
}
