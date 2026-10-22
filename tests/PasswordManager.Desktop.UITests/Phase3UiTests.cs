using System.IO;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Xunit;

namespace PasswordManager.Desktop.UITests;

/// <summary>
/// Phase 3 E2E ( FlaUI/UIA3): picker → create vault with recovery confirmation → add credential →
/// search → lock → unlock. Uses a temp home-compatible location; restores settings afterwards.
/// </summary>
public class Phase3UiTests
{
    private const string MasterPassword = "e2e-رمز-Password1!";

    [Fact]
    public void Full_Flow_CreateVault_AddCredential_Search_Lock_Unlock()
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, "PasswordManager.Desktop.exe");
        Assert.True(File.Exists(exePath), $"Desktop output not found: {exePath}");

        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ganjineh", "settings.json");
        string? originalSettings = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
        var vaultsPath = settingsPath.Replace("settings.json", "vaults.json");
        string? originalVaults = File.Exists(vaultsPath) ? File.ReadAllText(vaultsPath) : null;

        using var app = FlaUI.Core.Application.Launch(exePath);
        try
        {
            using var automation = new UIA3Automation();
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(30))
                ?? throw new TimeoutException("Main window did not appear.");

            // ── Vault picker: create a new vault ──
            Click(window, "PickerCreate");
            Type(window, "CreateName", "تست E2E");
            Type(window, "CreatePassword", MasterPassword);
            Type(window, "CreatePasswordConfirm", MasterPassword);
            Click(window, "CreateNext");

            // ── Recovery confirmation: read key + prompt, compute the challenged group ──
            var recoveryKey = WaitForText(window, "RecoveryKeyText", TimeSpan.FromSeconds(15));
            Assert.StartsWith("PMRK1-", recoveryKey);
            var prompt = WaitForText(window, "RecoveryChallengePrompt", TimeSpan.FromSeconds(5));
            var match = Regex.Match(prompt, @"(\d+)");
            Assert.True(match.Success, $"Challenge prompt without group number: {prompt}");
            var group = int.Parse(match.Groups[1].Value);
            Type(window, "RecoveryChallengeInput", GroupFor(group, recoveryKey));
            Click(window, "RecoveryConfirm");

            // ── Workspace: add a credential ──
            WaitFor(window, "AddCredential", TimeSpan.FromSeconds(15));
            Click(window, "AddCredential");
            Type(window, "EditorTitle", "بانک ملی — پرتال");
            Type(window, "EditorUsername", "user@example.com");
            Type(window, "EditorPassword", "SitePass-رمز-99!");
            Type(window, "EditorWebsite", "https://example.com/login");
            Click(window, "EditorSave");

            var rowTitle = WaitForText(window, "RowTitle", TimeSpan.FromSeconds(10));
            Assert.Contains("بانک ملی", rowTitle);

            // ── Search (Persian, with Arabic-variant folding: national bank) ──
            Type(window, "SearchBox", "ملی");
            Retry.WhileFalse(
                () => window.FindFirstDescendant(cf => cf.ByAutomationId("RowTitle"))?.Name?
                    .Contains("بانک ملی", StringComparison.Ordinal) == true,
                TimeSpan.FromSeconds(10));
            Thread.Sleep(700); // let the debounce settle before selecting

            // ── Details: select the first row; password masked by default ──
            WaitFor(window, "CredentialList", TimeSpan.FromSeconds(5)).AsListBox().Select(0);
            WaitFor(window, "DetailPassword", TimeSpan.FromSeconds(10));
            var masked = WaitForText(window, "DetailPassword", TimeSpan.FromSeconds(5));
            Assert.DoesNotContain("SitePass", masked);
            Click(window, "CopyPassword");

            // ── Lock, then re-select the vault, unlock again with the master password ──
            Click(window, "LockVaultButton");
            WaitFor(window, "PickerCreate", TimeSpan.FromSeconds(15)); // lock returns to the picker
            WaitFor(window, "VaultList", TimeSpan.FromSeconds(5)).AsListBox().Select(0);
            Click(window, "PickerUnlock");
            WaitFor(window, "UnlockPasswordBox", TimeSpan.FromSeconds(15));
            Type(window, "UnlockPasswordBox", MasterPassword);
            Click(window, "UnlockButton");

            Retry.WhileFalse(
                () => window.FindFirstDescendant(cf => cf.ByAutomationId("AddCredential")) is not null,
                TimeSpan.FromSeconds(20));
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

            if (originalVaults is not null)
            {
                File.WriteAllText(vaultsPath, originalVaults);
            }
            else if (File.Exists(vaultsPath))
            {
                File.Delete(vaultsPath);
            }
        }
    }

    private static string GroupFor(int oneBasedGroup, string recoveryKey)
    {
        var payload = recoveryKey["PMRK1-".Length..].Replace("-", string.Empty);
        return payload.Substring((oneBasedGroup - 1) * 4, 4);
    }

    private static void Click(Window window, string automationId) =>
        WaitFor(window, automationId, TimeSpan.FromSeconds(10)).AsButton().Invoke();

    private static void Type(Window window, string automationId, string text)
    {
        var element = WaitFor(window, automationId, TimeSpan.FromSeconds(10));
        element.AsTextBox().Text = text;
    }

    private static string WaitForText(Window window, string automationId, TimeSpan timeout)
    {
        _ = Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)), timeout).Result
            ?? throw new TimeoutException($"Element '{automationId}' not found.");
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            var text = element?.Name;
            if (element is not null && !string.IsNullOrEmpty(text))
            {
                return text;
            }

            Thread.Sleep(150);
        }

        throw new TimeoutException($"No text on '{automationId}' within {timeout}.");
    }

    private static AutomationElement WaitFor(Window window, string automationId, TimeSpan timeout) =>
        Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)), timeout).Result
        ?? throw new TimeoutException($"Element '{automationId}' not found within {timeout}.");
}
