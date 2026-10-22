using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using PasswordManager.Application.Abstractions;
using PasswordManager.Application.Credentials;
using PasswordManager.Application.Vaults;

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
/// </summary>
public sealed partial class AppViewModel : ObservableObject
{
    private readonly IVaultSessionManager _sessions;
    private readonly ILocalizer _localizer;
    private readonly IAppSettingsStore _settings;

    public VaultPickerViewModel Picker { get; }
    public CreateVaultViewModel Create { get; }
    public UnlockViewModel Unlock { get; }
    public VaultWorkspaceViewModel Workspace { get; }

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
        ICredentialService credentials,
        IAppSettingsStore settings,
        ILocalizer localizer,
        IDialogService dialogs,
        Services.ClipboardService clipboard)
    {
        _sessions = sessions;
        _localizer = localizer;
        _settings = settings;
        ShellFlowDirection = localizer.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        localizer.CultureChanged += OnCultureChanged;

        Picker = new VaultPickerViewModel(catalog, dialogs, localizer);
        Create = new CreateVaultViewModel(lifecycle, localizer);
        Unlock = new UnlockViewModel(lifecycle, localizer);
        Workspace = new VaultWorkspaceViewModel(lifecycle, sessions, credentials, clipboard, localizer, catalog);

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

    private void OnCultureChanged(object? sender, EventArgs e) =>
        ShellFlowDirection = _localizer.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
}
