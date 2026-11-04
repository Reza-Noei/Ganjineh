using System.Windows;
using System.Windows.Input;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Ganjineh.Application.Abstractions;
using Ganjineh.Application.Backup;
using Ganjineh.Application.Credentials;
using Ganjineh.Application.Import;
using Ganjineh.Application.Items;
using Ganjineh.Application.Security;
using Ganjineh.Application.Vaults;
using Ganjineh.Cryptography.Generation;
using Ganjineh.Desktop.Services;
using Ganjineh.Desktop.ViewModels;
using Ganjineh.Infrastructure.Localization;
using Ganjineh.Infrastructure.Paths;
using Ganjineh.Infrastructure.Settings;
using Ganjineh.Persistence.Registry;
using Velopack;

namespace Ganjineh.Desktop;

public partial class App : System.Windows.Application
{
    /// <summary>
    /// Composition-root locator, populated in <see cref="OnStartup"/>. Null only at design time,
    /// where markup extensions fall back to raw keys.
    /// </summary>
    public static IServiceProvider? Services { get; private set; }

    /// <summary>
    /// Velopack entry hooks (install/update/uninstall/first-run) MUST run before any WPF work —
    /// after an update the app may be relaunched into a hook that swaps binaries. That is why
    /// StartupObject points at this Main instead of the XAML-generated one.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

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
        services.AddSingleton<IBrowserImportService, BrowserImportService>();
        services.AddSingleton<AutoLockService>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<BackupViewModel>();
        services.AddSingleton<BrowserImportViewModel>();
        services.AddSingleton<Application.Abstractions.IAppUpdateService, Infrastructure.Updates.VelopackUpdateService>();
        services.AddSingleton<UpdateViewModel>();
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

        // Quiet update-availability check on startup: never downloads, never installs —
        // the result only surfaces in Settings → Updates (user-initiated from there).
        _ = Services.GetRequiredService<UpdateViewModel>().CheckOnStartupCommand.ExecuteAsync(null);

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
