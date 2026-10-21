using PasswordManager.Application.Abstractions;
using Xunit;

namespace PasswordManager.Application.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_Are_Persian_Light_And_FirstRun()
    {
        var settings = new AppSettings();

        Assert.Equal("fa", settings.Language);
        Assert.Equal("Light", settings.Theme);
        Assert.False(settings.FirstRunCompleted);
    }

    [Fact]
    public void With_Expression_Preserves_Unset_Values()
    {
        var updated = new AppSettings() with { Theme = "Dark" };

        Assert.Equal("fa", updated.Language);
        Assert.Equal("Dark", updated.Theme);
    }
}
