using System.Xml.Linq;
using Xunit;

namespace Ganjineh.IntegrationTests;

/// <summary>fa/en key parity: a missing English translation must fail the build's test run, not the UI.</summary>
public class LocalizationParityTests
{
    private static readonly string RepoRoot = LocateRepoRoot();

    private static string StringsPath(string fileName) =>
        Path.Combine(RepoRoot, "src", "Ganjineh.Infrastructure", "Localization", fileName);

    private static Dictionary<string, string> LoadKeys(string fileName)
    {
        var doc = XDocument.Load(StringsPath(fileName));
        return doc.Root!.Elements("data")
            .ToDictionary(e => e.Attribute("name")!.Value, e => e.Element("value")!.Value);
    }

    [Fact]
    public void Fa_And_En_Have_Identical_Key_Sets()
    {
        var fa = LoadKeys("Strings.resx");
        var en = LoadKeys("Strings.en.resx");

        var missingInEn = fa.Keys.Except(en.Keys).ToList();
        var missingInFa = en.Keys.Except(fa.Keys).ToList();

        Assert.True(missingInEn.Count == 0, $"Keys missing in Strings.en.resx: {string.Join(", ", missingInEn)}");
        Assert.True(missingInFa.Count == 0, $"Keys missing in Strings.resx: {string.Join(", ", missingInFa)}");
    }

    [Fact]
    public void No_Value_Is_Empty_Or_Whitespace()
    {
        foreach (var (file, keys) in new[] { ("Strings.resx", LoadKeys("Strings.resx")), ("Strings.en.resx", LoadKeys("Strings.en.resx")) })
        {
            var empty = keys.Where(k => string.IsNullOrWhiteSpace(k.Value)).Select(k => k.Key).ToList();
            Assert.True(empty.Count == 0, $"Empty values in {file}: {string.Join(", ", empty)}");
        }
    }

    [Fact]
    public void Neutral_Culture_Is_Persian()
    {
        // U+06AF (گ) in the neutral file proves the invariant culture really is Persian.
        var title = LoadKeys("Strings.resx")["App.Title"];
        Assert.Contains("گ", title);
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
