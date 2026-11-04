using Ganjineh.Infrastructure.Updates;
using Xunit;

namespace Ganjineh.IntegrationTests;

/// <summary>
/// Release-distribution readiness: the Velopack update source must be the public HTTPS
/// repository, the dev/test context must never claim to self-update, and the single-source
/// version in the Desktop csproj must stay in sync with the packaged expectations.
/// </summary>
public class ReleaseReadinessTests
{
    [Fact]
    public void Update_Source_Is_The_Public_Https_Repository()
    {
        var service = new VelopackUpdateService();
        Assert.Equal("https://github.com/Reza-Noei/Ganjineh", service.UpdateSourceUrl);
        Assert.StartsWith("https://", service.UpdateSourceUrl);
    }

    [Fact]
    public void Unpackaged_Test_Run_Cannot_Self_Update()
    {
        // Tests run via dotnet test, not the Velopack-installed app: self-update must be off.
        var service = new VelopackUpdateService();
        Assert.False(service.CanUpdate);
        Assert.Equal("?", service.CurrentVersion);
    }

    [Fact]
    public void Desktop_Project_Single_Source_Version_Is_1_0_0()
    {
        // Walk up to the repo root from the test bin directory.
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Ganjineh.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        var csproj = File.ReadAllText(
            Path.Combine(dir!, "src", "Ganjineh.Desktop", "Ganjineh.Desktop.csproj"));
        Assert.Contains("<Version>1.0.0</Version>", csproj);
    }
}
