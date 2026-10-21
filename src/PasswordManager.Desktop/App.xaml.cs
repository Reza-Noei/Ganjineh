using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PasswordManager.Application.Abstractions;
using PasswordManager.Desktop.Services;
using PasswordManager.Desktop.ViewModels;
using PasswordManager.Infrastructure.Localization;
using PasswordManager.Infrastructure.Paths;
using PasswordManager.Infrastructure.Settings;





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
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        Services = services.BuildServiceProvider();

        var settings = Services.GetRequiredService<IAppSettingsStore>().Current;
        Services.GetRequiredService<ILocalizer>().SetCulture(settings.Language);
        Services.GetRequiredService<ThemeService>().Apply(settings.Theme);

        DispatcherUnhandledException += (_, args) =>
        {
            // Last-resort handler: never surface exception details (they may contain user data).
            var localizer = Services.GetRequiredService<ILocalizer>();
            MessageBox.Show(localizer["Error.Unexpected"], "Ganjineh", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        MainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow.Show();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (Services as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
