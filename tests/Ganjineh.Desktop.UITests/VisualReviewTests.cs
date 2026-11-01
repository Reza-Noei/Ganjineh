using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Exceptions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Xunit;

namespace PasswordManager.Desktop.UITests;

/// <summary>
/// Visual review harness (UI redesign 2026-10-23): drives the real app through every major
/// screen in fa/light, fa/dark and en/light, saving CopyFromScreen captures into
/// artifacts/ui-review. Per the PrintWindow gotcha (transposed glyph runs), only CopyFromScreen
/// after a verified foreground switch is trusted. Not part of acceptance — run explicitly:
///   dotnet test --filter FullyQualifiedName~VisualReviewTests
/// </summary>
public class VisualReviewTests
{
    private const string MasterPassword = "e2e-رمز-Password1!";

    [Fact]
    public void Capture_All_Screens()
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, "PasswordManager.Desktop.exe");
        Assert.True(File.Exists(exePath), $"Desktop output not found: {exePath}");

        var outputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "ui-review"));
        Directory.CreateDirectory(outputDir);

        TryMakeProcessDpiAware();

        // Isolate the user's real settings/vault registry (same pattern as Phase3UiTests) and
        // force fa/light for a deterministic first pass.
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var settingsPath = Path.Combine(appData, "Ganjineh", "settings.json");
        var vaultsPath = Path.Combine(appData, "Ganjineh", "vaults.json");
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
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(30))
                ?? throw new TimeoutException("Main window did not appear.");

            Capture(window, outputDir, "01-picker-fa-light");

            // ── create vault (leaves an empty workspace) ──
            Click(window, "PickerCreate");
            Type(window, "CreateName", "نمونهٔ ویژوال");
            Type(window, "CreatePassword", MasterPassword);
            Type(window, "CreatePasswordConfirm", MasterPassword);
            Capture(window, outputDir, "02-create-fa-light");
            Click(window, "CreateNext");

            var recoveryKey = WaitForText(window, "RecoveryKeyText", TimeSpan.FromSeconds(15));
            Capture(window, outputDir, "03-recovery-fa-light");
            var prompt = WaitForText(window, "RecoveryChallengePrompt", TimeSpan.FromSeconds(5));
            var group = int.Parse(Regex.Match(prompt, @"(\d+)").Groups[1].Value);
            Type(window, "RecoveryChallengeInput", GroupFor(group, recoveryKey));
            Click(window, "RecoveryConfirm");

            WaitFor(window, "AddCredential", TimeSpan.FromSeconds(15));
            Thread.Sleep(600);
            Capture(window, outputDir, "04-workspace-empty-fa-light");

            // ── editor (login fields) ──
            Click(window, "AddCredential");
            Type(window, "EditorTitle", "بانک ملی — پرتال");
            Type(window, "EditorUsername", "user@example.com");
            Type(window, "EditorPassword", "SitePass-رمز-99!");
            Type(window, "EditorWebsite", "https://example.com/login");
            Capture(window, outputDir, "05-editor-fa-light");
            Click(window, "EditorSave");

            // ── details ──
            Thread.Sleep(800);
            WaitFor(window, "CredentialList", TimeSpan.FromSeconds(10)).AsListBox().Select(0);
            WaitFor(window, "DetailPassword", TimeSpan.FromSeconds(10));
            Thread.Sleep(400);
            Capture(window, outputDir, "06-details-fa-light");

            // ── generator ──
            Click(window, "OpenGenerator");
            WaitFor(window, "GeneratorOutput", TimeSpan.FromSeconds(5));
            Thread.Sleep(300);
            Capture(window, outputDir, "07-generator-fa-light");
            Click(window, "CloseGenerator");
            Thread.Sleep(300);

            // ── security center ──
            ScrollNav(window, 100); // the tools group sits below the fold
            Click(window, "OpenSecurityCenter");
            Thread.Sleep(2500); // analysis runs async
            Capture(window, outputDir, "08-security-fa-light");
            ScrollNav(window, 0);
            Click(window, "NavAllItems");
            Thread.Sleep(500);

            // ── settings overlay ──
            ScrollNav(window, 100);
            Click(window, "NavSettings");
            if (!WaitOptional(window, "SettingsCloseButton", TimeSpan.FromSeconds(3)))
            {
                // Invoke on this button can silently no-op; fall back to a real mouse click.
                MouseClick(window, "NavSettings");
                WaitFor(window, "SettingsCloseButton", TimeSpan.FromSeconds(5));
            }

            Capture(window, outputDir, "09-settings-fa-light");
            Click(window, "SettingsCloseButton");
            Thread.Sleep(300);

            // ── dark theme ──
            Click(window, "ToggleThemeButton");
            Thread.Sleep(700);
            WaitFor(window, "CredentialList", TimeSpan.FromSeconds(5)).AsListBox().Select(0);
            Thread.Sleep(400);
            Capture(window, outputDir, "10-details-fa-dark");
            Click(window, "ToggleThemeButton");
            Thread.Sleep(500);

            // ── English LTR pass ──
            Click(window, "ToggleLanguageButton");
            Thread.Sleep(900);
            WaitFor(window, "CredentialList", TimeSpan.FromSeconds(5)).AsListBox().Select(0);
            Thread.Sleep(400);
            Capture(window, outputDir, "11-details-en-light");

            Click(window, "NavSettings");
            if (!WaitOptional(window, "SettingsCloseButton", TimeSpan.FromSeconds(3)))
            {
                MouseClick(window, "NavSettings");
                WaitFor(window, "SettingsCloseButton", TimeSpan.FromSeconds(5));
            }

            Capture(window, outputDir, "12-settings-en-light");
            Click(window, "SettingsCloseButton");
            Thread.Sleep(300);

            // ── backup overlay + delete confirm (en) ──
            ScrollNav(window, 100);
            Click(window, "OpenBackup");
            WaitFor(window, "BackupCreate", TimeSpan.FromSeconds(5));
            Capture(window, outputDir, "13-backup-en-light");
            Click(window, "BackupCloseButton");
            Thread.Sleep(300);

            Click(window, "DeleteCredential");
            WaitFor(window, "DeleteConfirm", TimeSpan.FromSeconds(5));
            Capture(window, outputDir, "14-delete-confirm-en-light");
            Click(window, "DeleteCancel");
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

    private static void Capture(Window window, string dir, string name)
    {
        window.Focus();
        var hwnd = new IntPtr(window.Properties.NativeWindowHandle.Value);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (GetForegroundWindow() != hwnd && DateTime.UtcNow < deadline)
        {
            SetForegroundWindow(hwnd);
            Thread.Sleep(120);
        }

        if (!GetWindowRect(hwnd, out var rect))
        {
            throw new InvalidOperationException($"GetWindowRect failed for '{name}'.");
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size);
        }

        bitmap.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
    }

    private static string GroupFor(int oneBasedGroup, string recoveryKey)
    {
        var payload = recoveryKey["PMRK1-".Length..].Replace("-", string.Empty);
        return payload.Substring((oneBasedGroup - 1) * 4, 4);
    }

    private static void Click(Window window, string automationId)
    {
        var element = WaitFor(window, automationId, TimeSpan.FromSeconds(10));

        // Sidebar entries can sit below the fold; bring them into view first.
        element.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
        Thread.Sleep(150);

        // Buttons support Invoke; nav radios don't (WPF RadioButton exposes SelectionItem whose
        // Select() is unsupported) — those get a real mouse click, which needs the element on
        // screen and the window foreground.
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

    private static void MouseClick(Window window, string automationId)
    {
        var element = WaitFor(window, automationId, TimeSpan.FromSeconds(5));
        element.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
        Thread.Sleep(150);
        window.Focus();
        var point = element.GetClickablePoint();
        Mouse.MoveTo(point);
        Mouse.Click();
    }

    /// <summary>Scrolls the nav sidebar (plain ScrollViewer — children expose no ScrollItem).</summary>
    private static void ScrollNav(Window window, double verticalPercent)
    {
        var scroller = window.FindFirstDescendant(cf => cf.ByAutomationId("NavScroller"));
        scroller?.Patterns.Scroll.PatternOrDefault?.SetScrollPercent(-1, verticalPercent);
        Thread.Sleep(200);
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    /// <summary>Best-effort per-monitor-v2 awareness so CopyFromScreen yields physical pixels.</summary>
    private static void TryMakeProcessDpiAware()
    {
        try
        {
            _ = SetProcessDpiAwarenessContext(new IntPtr(-4));
        }
        catch (EntryPointNotFoundException)
        {
            // Older host without the API — captures fall back to the host's awareness.
        }
    }

    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
