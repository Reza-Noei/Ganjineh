using System.Xml.Linq;
using Xunit;

namespace Ganjineh.IntegrationTests;

/// <summary>Light/Dark must expose exactly the same resource keys or live theme swaps break.</summary>
public class ThemeParityTests
{
    private static readonly string RepoRoot = LocateRepoRoot();

    private static readonly string[] RequiredBrushTokens =
    [
        "Bg.Base", "Bg.Card", "Bg.Sunken", "Text.Primary", "Text.Secondary",
        "Accent", "Accent.Soft", "Danger", "Success", "Warning", "Divider",
    ];

    private static HashSet<string> ResourceKeys(string themeFile)
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot, "src", "Ganjineh.Desktop", "Themes", themeFile));
        var xKey = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");
        return doc.Descendants()
            .Where(e => e.Attribute(xKey) is not null)
            .Select(e => e.Attribute(xKey)!.Value)
            .ToHashSet();
    }

    [Fact]
    public void Light_And_Dark_Define_Identical_Key_Sets()
    {
        var light = ResourceKeys("Light.xaml");
        var dark = ResourceKeys("Dark.xaml");

        var onlyInLight = light.Except(dark).ToList();
        var onlyInDark = dark.Except(light).ToList();

        Assert.True(onlyInLight.Count == 0, $"Keys only in Light.xaml: {string.Join(", ", onlyInLight)}");
        Assert.True(onlyInDark.Count == 0, $"Keys only in Dark.xaml: {string.Join(", ", onlyInDark)}");
    }

    [Fact]
    public void All_Documented_Tokens_Exist_As_Brushes()
    {
        var keys = ResourceKeys("Light.xaml");

        foreach (var token in RequiredBrushTokens)
        {
            Assert.True(keys.Contains(token), $"Missing theme brush token: {token}");
        }
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
