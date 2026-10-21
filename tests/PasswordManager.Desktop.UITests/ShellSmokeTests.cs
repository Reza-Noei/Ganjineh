using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Xunit;

namespace PasswordManager.Desktop.UITests;

/// <summary>
/// Phase 1 UI smoke (roadmap exit criterion): the shell launches in Persian RTL, the language
/// toggle flips the nav to English live, and a theme swap does not destabilize the shell.
/// Restores the real settings file afterwards so the test leaves no preferences behind.
/// </summary>
public class ShellSmokeTests
{
    [Fact]
    public void Shell_Launches_In_Persian_And_Switches_Language_And_Theme()
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, "PasswordManager.Desktop.exe");
        Assert.True(File.Exists(exePath), $"Desktop output not found next to tests (build Desktop first): {exePath}");

        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ganjineh", "settings.json");
        string? originalSettings = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;

        // Fully qualified: bare "Application" resolves to the PasswordManager.Application project
        // namespace from inside any PasswordManager.* namespace.
        using var app = FlaUI.Core.Application.Launch(exePath);
        try
        {
            using var automation = new UIA3Automation();
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(30))
                ?? throw new TimeoutException("Main window did not appear within 30 s.");

            var allItems = WaitFor(window, "Nav.AllItems", TimeSpan.FromSeconds(15));
            Assert.Contains("همه", allItems.Name);

            WaitFor(window, "LanguageToggle", TimeSpan.FromSeconds(5)).AsButton().Invoke();
            Retry.WhileFalse(
                () => window.FindFirstDescendant(cf => cf.ByAutomationId("Nav.AllItems"))?.Name?
                    .Contains("All items", StringComparison.Ordinal) == true,
                TimeSpan.FromSeconds(15));
            Assert.Contains("All items", Find(window, "Nav.AllItems").Name);

            WaitFor(window, "ThemeToggle", TimeSpan.FromSeconds(5)).AsButton().Invoke();

            // Theme swap must not destabilize the shell: elements remain discoverable afterwards.
            WaitFor(window, "Nav.Settings", TimeSpan.FromSeconds(5));
        }
        finally
        {
            app.Kill();
            if (originalSettings is not null)
            {
                File.WriteAllText(settingsPath, originalSettings);
            }
            else if (File.Exists(settingsPath))
            {
                File.Delete(settingsPath);
            }
        }
    }

    private static AutomationElement Find(Window window, string automationId) =>
        window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))
        ?? throw new TimeoutException($"Element '{automationId}' not found.");

    private static AutomationElement WaitFor(Window window, string automationId, TimeSpan timeout) =>
        Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)), timeout).Result
        ?? throw new TimeoutException($"Element '{automationId}' not found within {timeout}.");
}
