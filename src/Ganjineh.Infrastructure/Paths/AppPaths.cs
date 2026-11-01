using Ganjineh.Application.Abstractions;

namespace Ganjineh.Infrastructure.Paths;

/// <summary>
/// Resolves the application data root per ADR-006: portable mode when a Data directory exists
/// beside the executable; otherwise %APPDATA%\Ganjineh. Never registers anything machine-bound.
/// </summary>
public sealed class AppPaths : IAppPaths
{
    private AppPaths(bool isPortable, string rootDir)
    {
        IsPortable = isPortable;
        RootDir = rootDir;
    }

    public bool IsPortable { get; }

    public string RootDir { get; }

    public string SettingsFilePath => Path.Combine(RootDir, "settings.json");

    public static AppPaths Resolve(string? baseDirectory = null, string? appDataDirectory = null)
    {
        var baseDir = baseDirectory ?? AppContext.BaseDirectory;
        var portableData = Path.Combine(baseDir, "Data");
        if (Directory.Exists(portableData))
        {
            return new AppPaths(isPortable: true, portableData);
        }

        var appData = appDataDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new AppPaths(isPortable: false, Path.Combine(appData, "Ganjineh"));
    }
}
