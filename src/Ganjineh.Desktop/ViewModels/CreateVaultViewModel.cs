using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ganjineh.Application.Abstractions;
using Ganjineh.Application.Vaults;
using Ganjineh.Cryptography.Recovery;

namespace Ganjineh.Desktop.ViewModels;

/// <summary>Guided vault creation: name → location → password → recovery confirmation.</summary>
public partial class CreateVaultViewModel : ObservableObject
{
    private readonly IVaultLifecycle _lifecycle;
    private readonly ILocalizer _localizer;

    public CreateVaultViewModel(IVaultLifecycle lifecycle, ILocalizer localizer)
    {
        _lifecycle = lifecycle;
        _localizer = localizer;
        _location = DefaultLocation();
    }

    private static string DefaultLocation()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return System.IO.Path.Combine(documents, "Ganjineh", "Vaults");
    }

    [ObservableProperty] private string _vaultName = string.Empty;
    [ObservableProperty] private string _location;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _error;

    // Recovery reveal state (step 2 of the wizard).
    [ObservableProperty] private bool _recoveryStage;
    [ObservableProperty] private string _recoveryKey = string.Empty;
    [ObservableProperty] private int _challengeGroup;
    [ObservableProperty] private string _challengeInput = string.Empty;
    [ObservableProperty] private string? _challengeError;

    /// <summary>Localized challenge prompt with the group number (invariant digits for testability).</summary>
    public string ChallengePrompt => string.Format(
        CultureInfo.InvariantCulture, _localizer["Create.RecoveryChallenge"], ChallengeGroup);

    partial void OnChallengeGroupChanged(int value) => OnPropertyChanged(nameof(ChallengePrompt));

    private char[]? _password;
    private char[]? _passwordConfirm;
    private CreatedVault? _created;

    public event EventHandler<VaultSession>? Completed;

    public string StrengthHint => EstimateStrength(_password);

    /// <summary>Called by the shell with both PasswordBox contents — never stored on properties.</summary>
    public void SetPasswords(char[] password, char[] confirm)
    {
        if (_password is not null)
        {
            Array.Clear(_password);
        }

        if (_passwordConfirm is not null)
        {
            Array.Clear(_passwordConfirm);
        }

        _password = password;
        _passwordConfirm = confirm;
        OnPropertyChanged(nameof(StrengthHint));
    }

    public bool HasPassword => _password is not null && _password.Length > 0;

    [RelayCommand]
    private void Browse()
    {
        // Location picking is wired by the shell (code-behind owns the dialog call).
        BrowseRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? BrowseRequested;

    [RelayCommand]
    private async Task CreateAsync()
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(VaultName))
        {
            Error = _localizer["Validation.TitleRequired"];
            return;
        }

        if (!HasPassword)
        {
            Error = _localizer["Validation.PasswordRequired"];
            return;
        }

        if (_passwordConfirm is null || !_password.AsSpan().SequenceEqual(_passwordConfirm))
        {
            Error = _localizer["Create.PasswordMismatch"];
            return;
        }

        if (Directory.Exists(System.IO.Path.Combine(Location, Guid.NewGuid() + ".pvault")))
        {
            Error = _localizer["Error.Storage"];
            return;
        }

        IsBusy = true;
        try
        {
            _created = await _lifecycle.CreateAsync(
                new CreateVaultRequest { Directory = Location, MasterPassword = _password! });
            _ = _lifecycle.RenameVaultAsync(_created.Session, VaultName.Trim());

            // Show the recovery key and require a transcription challenge.
            RecoveryKey = _created.RecoveryKeyEncoded;
            ChallengeGroup = RandomNumberGenerator.GetInt32(1, 15);
            RecoveryStage = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PolicyViolationException)
        {
            Error = _localizer["Error.Storage"];
        }
        finally
        {
            IsBusy = false;
        }

        // The confirm buffer has served its purpose; the master password stays until completion.
        if (_passwordConfirm is not null)
        {
            Array.Clear(_passwordConfirm);
            _passwordConfirm = null;
        }
    }

    [RelayCommand]
    private void ConfirmRecoverySaved()
    {
        var expected = GroupFor(ChallengeGroup, RecoveryKey);
        var typed = ChallengeInput.Trim().ToUpperInvariant().Replace("-", string.Empty).Replace(" ", string.Empty);
        if (!string.Equals(expected, typed, StringComparison.Ordinal))
        {
            ChallengeError = _localizer["Create.RecoveryChallengeMismatch"];
            return;
        }

        var session = _created!.Session;
        CleanupCreatedState();
        Completed?.Invoke(this, session);
    }

    [RelayCommand]
    private void Cancel()
    {
        if (_created is not null)
        {
            // A created-but-unconfirmed vault is kept (recovery shown); returning to the picker
            // locks it. Deleting automatically would risk destroying a working vault.
            _created.Session.Lock();
            _created = null;
        }

        CleanupCreatedState();
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Cancelled;

    private void CleanupCreatedState()
    {
        RecoveryKey = string.Empty;
        ChallengeInput = string.Empty;
        RecoveryStage = false;
        _password = null;
    }

    internal static string GroupFor(int oneBasedGroup, string recoveryKey)
    {
        var payload = recoveryKey["PMRK1-".Length..].Replace("-", string.Empty);
        var start = (oneBasedGroup - 1) * 4;
        return payload.Substring(start, 4);
    }

    /// <summary>Coarse strength heuristic until the zxcvbn-based estimator lands (Phase 5).</summary>
    internal static string EstimateStrength(char[]? password)
    {
        if (password is null || password.Length == 0)
        {
            return string.Empty;
        }

        int classes = 0;
        if (password.Any(char.IsLower)) classes++;
        if (password.Any(char.IsUpper)) classes++;
        if (password.Any(char.IsDigit)) classes++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) classes++;
        var score = password.Length + classes * 6 + (password.Any(c => c > 0x7F) ? 6 : 0);
        return score switch
        {
            < 18 => "Weak",
            < 30 => "Medium",
            _ => "Strong",
        };
    }
}
