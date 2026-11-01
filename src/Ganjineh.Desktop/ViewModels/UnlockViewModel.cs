using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ganjineh.Application.Abstractions;
using Ganjineh.Application.Vaults;
using Ganjineh.Cryptography.Errors;

namespace Ganjineh.Desktop.ViewModels;

public partial class UnlockViewModel : ObservableObject
{
    private readonly IVaultLifecycle _lifecycle;
    private readonly ILocalizer _localizer;

    public UnlockViewModel(IVaultLifecycle lifecycle, ILocalizer localizer)
    {
        _lifecycle = lifecycle;
        _localizer = localizer;
    }

    [ObservableProperty] private string _vaultDisplayName = string.Empty;
    [ObservableProperty] private string _vaultPath = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _recoveryMode;
    [ObservableProperty] private string _recoveryKey = string.Empty;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private int _failedAttempts;

    private char[]? _password;
    private VaultRow? _vault;

    public event EventHandler<VaultSession>? Unlocked;
    public event EventHandler? BackRequested;

    public void Prepare(VaultRow vault)
    {
        _vault = vault;
        VaultDisplayName = vault.DisplayName;
        VaultPath = vault.Path;
        RecoveryMode = false;
        Error = null;
        FailedAttempts = 0;
        ClearPassword();
    }

    /// <summary>Called by the shell with the PasswordBox content — never stored on a property.</summary>
    public void SetPassword(char[] password) => _password = password;

    public void ClearPassword()
    {
        if (_password is not null)
        {
            Array.Clear(_password);
            _password = null;
        }
    }

    [RelayCommand]
    private async Task UnlockAsync()
    {
        if (_vault is null || IsBusy)
        {
            return;
        }

        Error = null;
        if (_password is null || _password.Length == 0)
        {
            Error = _localizer["Validation.PasswordRequired"];
            return;
        }

        IsBusy = true;
        try
        {
            var session = await _lifecycle.UnlockWithPasswordAsync(_vault.Path, _password);
            ClearPassword();
            Unlocked?.Invoke(this, session);
        }
        catch (CryptoAuthenticationException)
        {
            FailedAttempts++;
            Error = _localizer["Unlock.WrongPassword"];
        }
        catch (Ganjineh.Persistence.StorageIoException)
        {
            Error = _localizer["Error.VaultMissing"];
        }
        catch (Ganjineh.Cryptography.Errors.UnsupportedFormatException)
        {
            Error = _localizer["Error.UnsupportedFormat"];
        }
        finally
        {
            IsBusy = false;
            ClearPassword();
        }
    }

    [RelayCommand]
    private async Task UnlockWithRecoveryAsync()
    {
        if (_vault is null || IsBusy)
        {
            return;
        }

        Error = null;
        if (string.IsNullOrWhiteSpace(RecoveryKey))
        {
            Error = _localizer["Validation.RecoveryRequired"];
            return;
        }

        IsBusy = true;
        try
        {
            var session = await _lifecycle.UnlockWithRecoveryKeyAsync(_vault.Path, RecoveryKey);
            RecoveryKey = string.Empty;
            Unlocked?.Invoke(this, session);
        }
        catch (Ganjineh.Cryptography.Errors.CryptoFormatException ex)
        {
            Error = _localizer["Validation.RecoveryInvalid"];
            _ = ex;
        }
        catch (CryptoAuthenticationException)
        {
            Error = _localizer["Unlock.WrongPassword"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleRecoveryMode()
    {
        RecoveryMode = !RecoveryMode;
        Error = null;
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);
}
