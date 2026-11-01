using System.IO;
using System.Text.Json;
using Ganjineh.Application.Abstractions;

namespace Ganjineh.Infrastructure.Settings;

/// <summary>
/// JSON settings store. The file is tiny and non-sensitive, so persistence is synchronous with a
/// temp-file move; a corrupt file falls back to defaults rather than blocking startup.
/// </summary>
public sealed class AppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public AppSettingsStore(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _filePath = paths.SettingsFilePath;
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public void Update(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tempPath, _filePath, overwrite: true);
        Current = settings;
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath)) ?? new AppSettings();
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Corrupt settings fall back to defaults; user preferences are not worth a crash.
        }
        catch (IOException)
        {
        }

        return new AppSettings();
    }
}
