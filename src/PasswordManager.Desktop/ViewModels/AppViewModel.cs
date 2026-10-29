using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Application.Abstractions;
using PasswordManager.Application.Backup;
using PasswordManager.Application.Items;
using PasswordManager.Application.Security;
using PasswordManager.Application.Vaults;
using PasswordManager.Cryptography.Generation;

namespace PasswordManager.Desktop.ViewModels;

public enum AppScreen
{
    VaultPicker,
    CreateVault,
    Unlock,
    Workspace,
}

/// <summary>
/// Root navigation: vault picker → (create | unlock) → workspace → lock → picker.
/// All session-bound state lives in the workspace VM, which clears itself on SessionChanged.
/// Settings and backup are app-level overlay VMs hosted by the shell window.
/// </summary>
public sealed partial class AppViewModel : ObservableObject
{
    private readonly IVaultSessionManager _sessions;
    private readonly ILocalizer _localizer;
    private readonly IAppSettingsStore _settings;
    private readonly Services.ThemeService _theme;

    public VaultPickerViewModel Picker { get; }
    public CreateVaultViewModel Create { get; }
    public UnlockViewModel Unlock { get; }
    public VaultWorkspaceViewModel Workspace { get; }
    public SettingsViewModel Settings { get; }
    public BackupViewModel Backup { get; }

    /// <summary>fa renders right-to-left; en mirrors the shell to left-to-right.</summary>
    public FlowDirection ShellFlowDirection { get; private set; } = FlowDirection.RightToLeft;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkspaceActive))]
    [NotifyPropertyChangedFor(nameof(CurrentView))]
    private AppScreen _screen = AppScreen.VaultPicker;

    public bool IsWorkspaceActive => Screen == AppScreen.Workspace;

    /// <summary>Content for the shell's templated ContentControl.</summary>
    public ObservableObject? CurrentView => Screen switch
    {
        AppScreen.VaultPicker => Picker,
        AppScreen.CreateVault => Create,
        AppScreen.Unlock => Unlock,
        AppScreen.Workspace => Workspace,
        _ => null,
    };

    public AppViewModel(
        IVaultLifecycle lifecycle,
        IVaultCatalog catalog,
        IVaultSessionManager sessions,
        IVaultItemService items,
        IFolderService folders,
        ITagService tags,
        IAttachmentService attachments,
        IPasswordGenerator passwordGenerator,
        IPassphraseGenerator passphraseGenerator,
        IVaultSecurityAnalyzer analyzer,
        IOtpApplicationService otp,
        IPasswordStrengthEstimator estimator,
        IAppSettingsStore settings,
        ILocalizer localizer,
        IDialogService dialogs,
        Services.ClipboardService clipboard,
        Services.ThemeService theme,
        IVaultBackupService backups)
    {
        _sessions = sessions;
        _localizer = localizer;
        _settings = settings;
        _theme = theme;
        ShellFlowDirection = localizer.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        localizer.CultureChanged += OnCultureChanged;

        Picker = new VaultPickerViewModel(catalog, dialogs, localizer);
        Create = new CreateVaultViewModel(lifecycle, localizer);
        Unlock = new UnlockViewModel(lifecycle, localizer);
        Workspace = new VaultWorkspaceViewModel(
            lifecycle, sessions, items, folders, tags, attachments,
            passwordGenerator, passphraseGenerator, analyzer, otp, estimator,
            clipboard, localizer);
        Settings = new SettingsViewModel(settings, localizer, theme);
        Backup = new BackupViewModel(backups, sessions, localizer, dialogs);

        Picker.CreateRequested += (_, _) => { Screen = AppScreen.CreateVault; };
        Picker.UnlockRequested += (_, row) =>
        {
            if (row is null)
            {
                return;
            }

            Unlock.Prepare(row);
            Screen = AppScreen.Unlock;
        };

        Create.Completed += (_, session) =>
        {
            ActivateWorkspace(session);
        };
        Create.Cancelled += (_, _) => { ShowPicker(); };

        Unlock.Unlocked += (_, session) => ActivateWorkspace(session);
        Unlock.BackRequested += (_, _) => ShowPicker();

        Workspace.LockRequested += (_, _) => ShowPicker();
    }

    public async Task InitializeAsync()
    {
        ShowPicker();
    }

    private void ActivateWorkspace(VaultSession session)
    {
        _sessions.Activate(session);
        Screen = AppScreen.Workspace;
        _ = Workspace.LoadAsync();
    }

    public void ShowPicker()
    {
        // Locking the current session wipes the workspace via SessionChanged.
        _sessions.LockCurrent();
        Screen = AppScreen.VaultPicker;
        Picker.Refresh();
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        ShellFlowDirection = _localizer.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        // Without this notification the window binding keeps the startup direction and the
        // shell never re-mirrors on a live fa/en switch.
        OnPropertyChanged(nameof(ShellFlowDirection));
    }

    // ── top-bar toggles (persist immediately; services react live) ──

    public bool ThemeIsDark => string.Equals(_theme.Current, "Dark", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void ToggleTheme()
    {
        var next = _theme.Toggle();
        _settings.Update(_settings.Current with { Theme = next });
        OnPropertyChanged(nameof(ThemeIsDark));
    }

    [RelayCommand]
    private void ToggleLanguage()
    {
        var next = _localizer.CurrentCulture.TwoLetterISOLanguageName.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? "fa"
            : "en";
        _localizer.SetCulture(next);
        _settings.Update(_settings.Current with { Language = next });
    }
}
