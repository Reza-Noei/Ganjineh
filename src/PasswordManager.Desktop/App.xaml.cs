using System.Windows;
using System.Windows.Input;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using PasswordManager.Application.Abstractions;
using PasswordManager.Application.Backup;
using PasswordManager.Application.Credentials;
using PasswordManager.Application.Items;
using PasswordManager.Application.Security;
using PasswordManager.Application.Vaults;
using PasswordManager.Cryptography.Generation;
using PasswordManager.Desktop.Services;
using PasswordManager.Desktop.ViewModels;
using PasswordManager.Infrastructure.Localization;
using PasswordManager.Infrastructure.Paths;
using PasswordManager.Infrastructure.Settings;
using PasswordManager.Persistence.Registry;

namespace PasswordManager.Desktop;

public partial class App : System.Windows.Application
{
    /// <summary>
    /// Composition-root locator, populated in <see cref="OnStartup"/>. Null only at design time,
    /// where markup extensions fall back to raw keys.
    /// </summary>
    public static IServiceProvider? Services { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppPaths>(AppPaths.Resolve());
        services.AddSingleton<IAppSettingsStore, AppSettingsStore>();
        services.AddSingleton<ILocalizer, LocalizationService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<DialogService>());
        services.AddSingleton<ClipboardService>();
        services.AddSingleton<Func<VaultRegistry>>(sp =>
        {
            var paths = sp.GetRequiredService<IAppPaths>();
            return () => new VaultRegistry(Path.Combine(paths.RootDir, "vaults.json"));
        });
        services.AddSingleton<IVaultCatalog, VaultCatalog>();
        services.AddSingleton<IVaultLifecycle, VaultLifecycleService>();
        services.AddSingleton<IVaultSessionManager, VaultSessionManager>();
        services.AddSingleton<ItemEncryption>();
        services.AddSingleton<ICredentialService, CredentialService>();
        services.AddSingleton<IVaultItemService, VaultItemService>();
        services.AddSingleton<IFolderService, FolderService>();
        services.AddSingleton<ITagService, TagService>();
        services.AddSingleton<IAttachmentService, AttachmentService>();
        services.AddSingleton<IPasswordGenerator, PasswordGenerator>();
        services.AddSingleton<IPassphraseGenerator, PassphraseGenerator>();
        services.AddSingleton<IVaultSecurityAnalyzer, VaultSecurityAnalyzer>();
        services.AddSingleton<IOtpApplicationService, OtpApplicationService>();
        services.AddSingleton<IPasswordStrengthEstimator, ZxcvbnStrengthEstimator>();
        services.AddSingleton<IVaultBackupService, VaultBackupService>();
        services.AddSingleton<AutoLockService>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<BackupViewModel>();
        services.AddSingleton<AppViewModel>();
        services.AddSingleton<MainWindow>();
        Services = services.BuildServiceProvider();

        var settings = Services.GetRequiredService<IAppSettingsStore>().Current;
        Services.GetRequiredService<ILocalizer>().SetCulture(settings.Language);
        Services.GetRequiredService<ThemeService>().Apply(settings.Theme);

        DispatcherUnhandledException += (_, args) =>
        {
            // Last-resort handler: never surface exception details (they may contain user data).
            try { System.IO.File.WriteAllText(Path.Combine(System.IO.Path.GetTempPath(), "ganjineh-ui-crash.log"), args.Exception.ToString()); }
            catch { /* diagnostics only */ }
            var localizer = Services.GetRequiredService<ILocalizer>();
            MessageBox.Show(localizer["Error.Unexpected"], "Ganjineh", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var autoLock = Services.GetRequiredService<AutoLockService>();
        var mainWindow = Services.GetRequiredService<MainWindow>();
        var appViewModel = Services.GetRequiredService<AppViewModel>();
        autoLock.LockRequested += (_, _) => mainWindow.Dispatcher.Invoke(() =>
        {
            if (appViewModel.Screen == AppScreen.Workspace)
            {
                appViewModel.ShowPicker();
            }
        });

        // Any UI input defers auto-lock.
        InputManager.Current.PreProcessInput += (_, args) =>
        {
            if (args.StagingItem?.Input is InputEventArgs)
            {
                autoLock.NotifyActivity();
            }
        };

        MainWindow = mainWindow;
        mainWindow.Show();
        _ = mainWindow.Dispatcher.InvokeAsync(() => _ = appViewModel.InitializeAsync());
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (Services as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
