using System.IO;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Xunit;

namespace Ganjineh.Desktop.UITests;

/// <summary>
/// Phase 9 shell E2E (FlaUI/UIA3): Settings → Import &amp; Export → the shared wizard with each
/// browser preconfigured → step navigation, dismissal, dark-theme render, and the Win32 open
/// dialog appearing on Browse. The full four-step journey (parse → preview → conflicts →
/// import → navigate → persistence) is covered at the wizard-viewmodel boundary in
/// BrowserImportViewModelJourneyTests — this host's modal-dialog UIA provider cannot be driven
/// reliably (see phase-9-0-test-results.md).
/// Synthetic credentials only.
/// </summary>
public class Phase9UiTests
{
    private const string MasterPassword = "e2e-رمز-Password1!";

    [Fact]
    public void Settings_ImportExport_Both_Wizards_Step_Navigate_And_Browse()
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, "Ganjineh.exe");
        Assert.True(File.Exists(exePath), $"Desktop output not found: {exePath}");

        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ganjineh", "settings.json");
        var vaultsPath = settingsPath.Replace("settings.json", "vaults.json");
        string? originalSettings = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
        string? originalVaults = File.Exists(vaultsPath) ? File.ReadAllText(vaultsPath) : null;
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath,
            "{\"Language\":\"fa\",\"Theme\":\"Light\",\"ClipboardTimeoutSeconds\":30,\"AutoLockMinutes\":10,\"FirstRunCompleted\":true}");
        if (File.Exists(vaultsPath))
        {
            File.Delete(vaultsPath);
        }

        using var app = FlaUI.Core.Application.Launch(exePath);
        try
        {
            using var automation = new UIA3Automation();
            automation.ConnectionTimeout = TimeSpan.FromSeconds(15);
            automation.TransactionTimeout = TimeSpan.FromSeconds(30);
            Window window = app.GetMainWindow(automation, TimeSpan.FromSeconds(30))
                ?? throw new TimeoutException("Main window did not appear.");

            // ── create a vault (standard journey) ──
            InvokeUntil(window, "PickerCreate", () => Exists(window, "CreateName"), "the create form");
            Type(window, "CreateName", "واردات E2E");
            Type(window, "CreatePassword", MasterPassword);
            Type(window, "CreatePasswordConfirm", MasterPassword);
            Click(window, "CreateNext");
            var recoveryKey = WaitForText(window, "RecoveryKeyText", TimeSpan.FromSeconds(15));
            var prompt = WaitForText(window, "RecoveryChallengePrompt", TimeSpan.FromSeconds(5));
            var group = int.Parse(System.Text.RegularExpressions.Regex.Match(prompt, @"(\d+)").Groups[1].Value);
            Type(window, "RecoveryChallengeInput", GroupFor(group, recoveryKey));
            Click(window, "RecoveryConfirm");
            WaitFor(window, "AddCredential", TimeSpan.FromSeconds(15));

            // ── Settings → Import category → Chrome wizard ──
            // (The 2026-11 settings redesign uses an internal category rail; the Import panel is
            // collapsed until its category is selected — same step a user takes.)
            InvokeUntil(window, "NavSettings", () => Exists(window, "SettingsCloseButton"), "the settings pane");
            InvokeUntil(window, "SettingsNavImport", () => Exists(window, "ImportFromChrome"), "the import panel");
            InvokeUntil(window, "ImportFromChrome", () => Exists(window, "ImportClose"), "the chrome wizard");
            WaitFor(window, "ImportClose", TimeSpan.FromSeconds(5));

            // Step 1 → 2, then dismiss; Esc is wired identically (TryCloseTopOverlay).
            InvokeUntil(window, "ImportInstructionsNext", () => Exists(window, "ImportBrowse"), "the file step");
            InvokeUntil(window, "ImportClose", () => !Exists(window, "ImportClose"), "wizard dismissal");

            // Firefox reopens the SAME wizard, preconfigured (title swaps via WizardTitle).
            InvokeUntil(window, "NavSettings", () => Exists(window, "SettingsCloseButton"), "the settings pane");
            InvokeUntil(window, "SettingsNavImport", () => Exists(window, "ImportFromFirefox"), "the import panel");
            InvokeUntil(window, "ImportFromFirefox", () => Exists(window, "ImportClose"), "the firefox wizard");
            InvokeUntil(window, "ImportInstructionsNext", () => Exists(window, "ImportBrowse"), "the file step");

            // Dark theme while the wizard is open: must render, not crash.
            Click(window, "ToggleThemeButton");
            Thread.Sleep(600);
            Assert.True(WaitOptional(window, "ImportClose", TimeSpan.FromSeconds(3)), "Wizard must survive the theme swap.");
            Click(window, "ToggleThemeButton");
            Thread.Sleep(600);

            // ── Browse raises the shell handler → the Win32 open dialog appears ──
            InvokeUntil(window, "ImportBrowse",
                () => FindDialogHwnd(app.ProcessId) != IntPtr.Zero, "the file dialog");
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

    // ── Win32 dialog discovery (the modal dialog's UIA provider is unusable under this host) ──

    private static IntPtr FindDialogHwnd(int processId)
    {
        IntPtr found = IntPtr.Zero;
        _ = EnumWindows((hwnd, lParam) =>
        {
            _ = GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != (uint)processId || !IsWindowVisible(hwnd))
            {
                return true;
            }

            var buffer = new System.Text.StringBuilder(256);
            _ = GetClassName(hwnd, buffer, buffer.Capacity);
            if (string.Equals(buffer.ToString(), "#32770", StringComparison.Ordinal))
            {
                found = hwnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder text, int count);

    // ── shared helpers (same recipes as Phase3UiTests/VisualReviewTests) ──

    private static string GroupFor(int oneBasedGroup, string recoveryKey)
    {
        var payload = recoveryKey["PMRK1-".Length..].Replace("-", string.Empty);
        return payload.Substring((oneBasedGroup - 1) * 4, 4);
    }

    private static bool Exists(Window window, string automationId) =>
        window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)) is not null;

    /// <summary>Non-throwing "element exists and shows text" check.</summary>
    private static bool NonEmptyText(Window window, string automationId) =>
        window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))?.Name is { Length: > 0 };

    /// <summary>Click with effect verification: UIA Invoke delivery on freshly-swapped
    /// templates is unreliable in this environment (single invokes can be swallowed outright),
    /// so re-send the invoke until the expected effect appears. The commands here are
    /// idempotent opens/toggles.</summary>
    private static void InvokeUntil(Window window, string automationId, Func<bool> effect, string effectName, int maxAttempts = 20)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                Click(window, automationId);
            }
            catch (TimeoutException)
            {
                // The element can vanish mid-transition (overlay switch); retry.
            }

            if (PollEffect(effect, TimeSpan.FromSeconds(3)))
            {
                return;
            }
        }

        Assert.Fail($"'{automationId}' never produced {effectName} after {maxAttempts} attempts.");
    }

    private static bool PollEffect(Func<bool> effect, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (effect())
            {
                return true;
            }

            Thread.Sleep(400);
        }

        return effect();
    }

    private static void Click(Window window, string automationId)
    {
        var element = WaitFor(window, automationId, TimeSpan.FromSeconds(10));
        element.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
        Thread.Sleep(150);
        var invoke = element.Patterns.Invoke.PatternOrDefault;
        if (invoke is not null)
        {
            invoke.Invoke();
            return;
        }

        var point = element.GetClickablePoint();
        Mouse.MoveTo(point);
        Mouse.Click();
    }

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

    private static bool WaitOptional(Window window, string automationId, TimeSpan timeout) =>
        Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)), timeout)
            .Result is not null;
}
