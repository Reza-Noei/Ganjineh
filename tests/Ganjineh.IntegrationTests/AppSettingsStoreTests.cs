using Ganjineh.Application.Abstractions;
using Ganjineh.Infrastructure.Paths;
using Ganjineh.Infrastructure.Settings;
using Xunit;

namespace Ganjineh.IntegrationTests;

public class AppSettingsStoreTests : IDisposable
{
    private readonly string _tempRoot = Directory.CreateTempSubdirectory("ganjineh-tests").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void RoundTrip_Persists_All_Fields()
    {
        var paths = AppPaths.Resolve(_tempRoot, Path.Combine(_tempRoot, "appdata"));
        var store = new AppSettingsStore(paths);

        store.Update(store.Current with { Language = "en", Theme = "Dark", FirstRunCompleted = true });

        var reloaded = new AppSettingsStore(paths);
        Assert.Equal("en", reloaded.Current.Language);
        Assert.Equal("Dark", reloaded.Current.Theme);
        Assert.True(reloaded.Current.FirstRunCompleted);
    }

    [Fact]
    public void Corrupt_File_Falls_Back_To_Defaults()
    {
        var paths = AppPaths.Resolve(_tempRoot, Path.Combine(_tempRoot, "appdata"));
        Directory.CreateDirectory(paths.RootDir);
        File.WriteAllText(paths.SettingsFilePath, "{ not json ");

        var store = new AppSettingsStore(paths);

        Assert.Equal("fa", store.Current.Language);
        Assert.Equal("Light", store.Current.Theme);
    }

    [Fact]
    public void Missing_File_Yields_Defaults()
    {
        var paths = AppPaths.Resolve(_tempRoot, Path.Combine(_tempRoot, "appdata"));

        var store = new AppSettingsStore(paths);

        Assert.Equal(new AppSettings(), store.Current);
    }

    [Fact]
    public void Portable_Mode_Is_Detected_By_Data_Directory_Marker()
    {
        var portableBase = Path.Combine(_tempRoot, "usb");
        Directory.CreateDirectory(Path.Combine(portableBase, "Data"));

        var portable = AppPaths.Resolve(portableBase, Path.Combine(_tempRoot, "appdata"));
        var installed = AppPaths.Resolve(Path.Combine(_tempRoot, "install"), Path.Combine(_tempRoot, "appdata"));

        Assert.True(portable.IsPortable);
        Assert.EndsWith("Data", portable.RootDir, StringComparison.OrdinalIgnoreCase);
        Assert.False(installed.IsPortable);
    }

    [Fact]
    public void Settings_File_Lives_Under_Root()
    {
        var paths = AppPaths.Resolve(_tempRoot, Path.Combine(_tempRoot, "appdata"));

        Assert.StartsWith(paths.RootDir, paths.SettingsFilePath, StringComparison.Ordinal);
        Assert.EndsWith("settings.json", paths.SettingsFilePath, StringComparison.Ordinal);
    }
}
