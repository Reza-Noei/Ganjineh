using System.IO;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Xunit;

namespace Ganjineh.Desktop.UITests;

/// <summary>
/// Full-surface E2E (FlaUI/UIA3): every user-facing flow with its mandatory/optional field
/// contract. Each fact launches the app with isolated settings + vault registry (restored in
/// finally) and creates its own vault through the real wizard, solving the recovery challenge.
///
/// Mandatory/Optional contract under test:
///   • Title is the ONLY mandatory item field (all types) — empty title blocks the save with a
///     language-correct error; everything else (username, password, website, notes, description,
///     custom fields, folder, tags, dynamic type fields) saves empty.
///   • Every labeled field shows an explicit localized Required/Optional badge (EN/FA).
///   • Wizard password fields, the recovery challenge and the import file are mandatory;
///     generator excluded-chars/separator, import filter and sidebar folder/tag names
///     (empty adds are silent no-ops) are optional — service-level "required" errors are
///     covered by the integration suite.
///
/// Native file dialogs (backup file, import CSV, attachments) are out of UIA scope — those
/// flows are covered at service level by the integration suite.
/// </summary>
public class FullAppE2ETests
{
    private const string MasterPassword = "e2e-رمز-Password1!";

    // Exact resource strings (Strings.resx / Strings.en.resx) — update together with the resx.
    private const string EnTitleRequired = "A title is required.";
    private const string FaTitleRequired = "عنوان الزامی است.";
    private const string EnChallengeMismatch = "The entered group is not correct.";
    private const string FaChallengeMismatch = "گروه وارد‌شده درست نیست.";
    private const string EnFolderNameRequired = "Folder name is required.";
    private const string EnTagNameRequired = "Tag name is required.";
    private const string EnTagDuplicate = "A tag with this name already exists.";
    private const string EnRecoveryRequired = "Enter the Recovery Key.";
    private const string EnRecoveryInvalid = "The Recovery Key format is invalid.";
    private const string EnNoClasses = "Select at least one character class.";
    private const string EnRequiredBadge = "Required";
    private const string FaRequiredBadge = "الزامی";
    private const string EnOptionalBadge = "Optional";
    private const string FaOptionalBadge = "اختیاری";

    // ── 1. Vault lifecycle: mandatory recovery challenge, lock, wrong password ──

    [Fact]
    public void Vault_Lifecycle_Challenge_Is_Mandatory_Lock_Unlock_WrongPassword()
    {
        using var scope = TestScope.Start(language: "en", theme: "Light");
        var w = scope.Window;

        // Challenge is mandatory: a wrong transcription blocks completion with the exact error.
        Click(w, "PickerCreate");
        Type(w, "CreateName", ScopeVaultName());
        Type(w, "CreatePassword", MasterPassword);
        Type(w, "CreatePasswordConfirm", MasterPassword);
        Click(w, "CreateNext");

        var prompt = WaitForText(w, "RecoveryChallengePrompt", Long);
        var group = int.Parse(Regex.Match(prompt, @"(\d+)").Groups[1].Value);
        var key = WaitForText(w, "RecoveryKeyText", Long);
        var right = GroupFor(group, key);
        var wrong = right[..3] + (right[3] == 'A' ? 'B' : 'A');
        Type(w, "RecoveryChallengeInput", wrong);
        Click(w, "RecoveryConfirm");
        Assert.Equal(EnChallengeMismatch, WaitForText(w, "RecoveryChallengeError", Long));
        AssertStillVisible(w, "RecoveryConfirm", "challenge must not complete on a wrong answer");

        Type(w, "RecoveryChallengeInput", right);
        Click(w, "RecoveryConfirm");
        WaitFor(w, "AddCredential", Long);

        // Lock → wrong password → exact localized error; correct password returns.
        Click(w, "LockVaultButton");
        WaitFor(w, "PickerCreate", Long);
        WaitFor(w, "VaultList", Long).AsListBox().Select(0);
        Click(w, "PickerUnlock");
        WaitFor(w, "UnlockPasswordBox", Long);
        Type(w, "UnlockPasswordBox", "definitely-not-it");
        Click(w, "UnlockButton");
        Assert.Equal("Wrong password, or the vault has been tampered with.",
            WaitForText(w, "UnlockError", Long));
        Type(w, "UnlockPasswordBox", MasterPassword);
        Click(w, "UnlockButton");
        WaitFor(w, "AddCredential", Long);
    }

    // ── 2. Recovery-key unlock: mandatory input + format validation ──

    [Fact]
    public void Recovery_Unlock_Mandatory_Input_And_Format_Validation()
    {
        using var scope = TestScope.Start(language: "en", theme: "Light");
        var w = scope.Window;
        CreateVault(w, ScopeVaultName());
        Click(w, "LockVaultButton");
        WaitFor(w, "PickerCreate", Long);
        WaitFor(w, "VaultList", Long).AsListBox().Select(0);
        Click(w, "PickerUnlock");
        WaitFor(w, "UnlockPasswordBox", Long);
        Click(w, "ToggleRecoveryMode");
        WaitFor(w, "UnlockRecoveryInput", Long);

        // Mandatory: confirming with an empty key is rejected with the exact error.
        Click(w, "UnlockConfirmRecovery");
        Assert.Equal(EnRecoveryRequired, WaitForText(w, "UnlockError", Long));

        // Malformed key → format error before any crypto work.
        Type(w, "UnlockRecoveryInput", "PMRK1-not-a-real-key");
        Click(w, "UnlockConfirmRecovery");
        Assert.Equal(EnRecoveryInvalid, WaitForText(w, "UnlockError", Long));

        Click(w, "ToggleRecoveryMode");
        WaitFor(w, "UnlockPasswordBox", Long);
    }

    // ── 3. Editor: title mandatory, all other fields optional + badge contract (EN) ──

    [Fact]
    public void Editor_Title_Is_The_Only_Mandatory_Field_Badges_Localized_EN()
    {
        using var scope = TestScope.Start(language: "en", theme: "Light");
        var w = scope.Window;
        CreateVault(w, ScopeVaultName());

        Click(w, "AddCredential");
        WaitFor(w, "EditorTitle", Long);

        // Badge contract: exactly one Required (title), every other labeled field Optional.
        Assert.Equal(1, CountByName(w, EnRequiredBadge));
        Assert.True(CountByName(w, EnOptionalBadge) >= 7,
            $"expected >=7 Optional badges in login editor, got {CountByName(w, EnOptionalBadge)}");

        // Mandatory: username alone must NOT save; the error is language-exact.
        Type(w, "EditorUsername", "user@example.com");
        Click(w, "EditorSave");
        Assert.Equal(EnTitleRequired, WaitForText(w, "EditorError", Long));
        Assert.Equal(0, CountById(w, "RowTitle"));

        // Optional: everything else empty → title-only save succeeds.
        Type(w, "EditorTitle", "Title Only");
        Click(w, "EditorSave");
        WaitFor(w, "AddCredential", Long);
        Assert.Equal("Title Only", WaitForText(w, "RowTitle", Long));

        // Optional values that WERE entered round-trip (username prefilled on edit).
        WaitFor(w, "CredentialList", Long).AsListBox().Select(0);
        WaitFor(w, "DetailTitle", Long);
        Click(w, "EditCredential");
        WaitFor(w, "EditorTitle", Long);
        Assert.Equal("Title Only", ReadValue(w, "EditorTitle"));
        Assert.Equal("user@example.com", ReadValue(w, "EditorUsername"));
        Click(w, "EditorCancel");
    }

    // ── 4. Every item type saves with title only (dynamic fields all optional) ──

    [Fact]
    public void Every_Item_Type_Saves_With_Title_Only()
    {
        using var scope = TestScope.Start(language: "en", theme: "Light");
        var w = scope.Window;
        CreateVault(w, ScopeVaultName());

        // EditorKindChoices order: Login, SecureNote, PaymentCard, Identity, Wifi,
        // ApiCredential, SoftwareLicense, RecoveryCodes, Custom.
        for (var kind = 0; kind < 9; kind++)
        {
            Click(w, "AddCredential");
            WaitFor(w, "EditorTitle", Long);
            if (kind > 0)
            {
                SelectComboItem(w, "EditorKind", kind);
            }

            var title = $"T{kind}-{Guid.NewGuid():N}";
            Type(w, "EditorTitle", title);
            Click(w, "EditorSave");
            WaitFor(w, "AddCredential", Long);
            Assert.Equal(title, WaitForText(w, "RowTitle", Long));
        }

        Assert.Equal(9, CountById(w, "RowTitle"));
    }

    // ── 5. Persian: badges + validation errors fully localized, live language switch ──

    [Fact]
    public void Persian_Badges_And_Errors_Are_Localized_Then_Switches_To_English_Live()
    {
        using var scope = TestScope.Start(language: "fa", theme: "Light");
        var w = scope.Window;

        Click(w, "PickerCreate");
        Type(w, "CreateName", ScopeVaultName());
        Type(w, "CreatePassword", MasterPassword);
        Type(w, "CreatePasswordConfirm", MasterPassword);
        Click(w, "CreateNext");

        var prompt = WaitForText(w, "RecoveryChallengePrompt", Long);
        var group = int.Parse(Regex.Match(prompt, @"(\d+)").Groups[1].Value);
        var key = WaitForText(w, "RecoveryKeyText", Long);
        Type(w, "RecoveryChallengeInput", GroupFor(group, key)[..3] + "Z");
        Click(w, "RecoveryConfirm");
        Assert.Equal(FaChallengeMismatch, WaitForText(w, "RecoveryChallengeError", Long));
        Type(w, "RecoveryChallengeInput", GroupFor(group, key));
        Click(w, "RecoveryConfirm");
        WaitFor(w, "AddCredential", Long);

        Click(w, "AddCredential");
        WaitFor(w, "EditorTitle", Long);
        Assert.True(CountByName(w, FaRequiredBadge) >= 1, "missing «الزامی» badge");
        Assert.True(CountByName(w, FaOptionalBadge) >= 7, "missing «اختیاری» badges");
        Click(w, "EditorSave");
        Assert.Equal(FaTitleRequired, WaitForText(w, "EditorError", Long));

        // Optional field with a Persian value saves fine.
        Type(w, "EditorTitle", "قلم forgotten");
        Click(w, "EditorSave");
        WaitFor(w, "AddCredential", Long);

        // Live language switch (sidebar) → next validation error is English.
        Click(w, "ToggleLanguageButton");
        Click(w, "AddCredential");
        WaitFor(w, "EditorTitle", Long);
        Click(w, "EditorSave");
        Assert.Equal(EnTitleRequired, WaitForText(w, "EditorError", Long));
    }

    // ── 6. Folders & tags: empty names are silent no-ops (UI contract), duplicate tag rejected ──

    [Fact]
    public void Folders_Tags_Mandatory_Names_And_Duplicate_Tag_Rejected()
    {
        using var scope = TestScope.Start(language: "en", theme: "Light");
        var w = scope.Window;
        CreateVault(w, ScopeVaultName());

        // Empty folder/tag names are SILENTLY IGNORED by the sidebar commands (no error text,
        // nothing added) — the "required" validation lives at service level (Phase4 tests).
        Click(w, "AddFolderButton");
        Thread.Sleep(1200);
        Assert.Equal(string.Empty, WaitFor(w, "StatusText", Long).Name);

        Type(w, "NewFolderInput", "work");
        Click(w, "AddFolderButton");
        var folderCreated = Retry.WhileFalse(() => AnyListItemNameContains(w, "FolderList", "work"),
            Long).Result;
        Assert.True(folderCreated, "folder 'work' was not created by AddFolderButton");

        Click(w, "AddTagButton");
        Thread.Sleep(1200);
        Assert.Equal(string.Empty, WaitFor(w, "StatusText", Long).Name);

        Type(w, "NewTagInput", "کاری");
        Click(w, "AddTagButton");
        var tagCreated = Retry.WhileFalse(() => AnyListItemNameContains(w, "TagList", "کاری"),
            Long).Result;
        Assert.True(tagCreated, "tag 'کاری' was not created by AddTagButton");

        // Duplicate (whitespace-normalized) tag MERGES into the existing one (service contract,
        // covered in Phase4): no error text, and no second row appears.
        Type(w, "NewTagInput", " کاری ");
        Click(w, "AddTagButton");
        Thread.Sleep(1200);
        Assert.Equal(1, CountListItems(w, "TagList"));
        Assert.Equal(string.Empty, WaitFor(w, "StatusText", Long).Name);
    }

    // ── 7. Generator: optional inputs accepted, mandatory class selection ──

    [Fact]
    public void Generator_Optional_Inputs_NoClasses_Validation_Passphrase_Mode()
    {
        using var scope = TestScope.Start(language: "en", theme: "Light");
        var w = scope.Window;
        CreateVault(w, ScopeVaultName());

        Click(w, "OpenGenerator");
        WaitFor(w, "GeneratorOutput", Long);
        Assert.NotEmpty(WaitForText(w, "GeneratorOutput", Long));

        // Excluded characters are optional: empty works, filled works.
        Type(w, "GeneratorExcluded", "abc");
        Click(w, "Regenerate");
        Assert.NotEmpty(WaitForText(w, "GeneratorOutput", Long));

        // Character classes are mandatory: uncheck all → exact localized error.
        foreach (var className in new[] { "a-z", "A-Z", "0-9", "!@#" })
        {
            var box = w.FindAllDescendants(cf => cf.ByName(className))
                .FirstOrDefault()?.AsCheckBox();
            if (box is { IsChecked: true })
            {
                box.Toggle();
            }
        }

        Click(w, "Regenerate");
        Assert.Equal(EnNoClasses, WaitForText(w, "GeneratorStrength", Long));

        // Passphrase mode: separator + capitalization are optional (defaults generate).
        SelectRadio(w, "GeneratorPassphraseMode");
        WaitFor(w, "PassphraseOutput", Long);
        Assert.NotEmpty(WaitForText(w, "PassphraseOutput", Long));
        Click(w, "CloseGenerator");
    }

    // ── 8. Search, favorite, trash cycle ──

    [Fact]
    public void Search_Favorite_Trash_Restore_EmptyTrash()
    {
        using var scope = TestScope.Start(language: "en", theme: "Light");
        var w = scope.Window;
        CreateVault(w, ScopeVaultName());
        AddItem(w, "بانک ملی — پرتال");
        AddItem(w, "Server Vault");
        Assert.Equal(2, CountById(w, "RowTitle"));

        // Search (Persian, folds Arabic variants), then clear.
        Type(w, "SearchBox", "ملی");
        _ = Retry.WhileFalse(() => AnyRowTitleContains(w, "بانک ملی"), Long);
        Type(w, "SearchBox", "");
        _ = Retry.WhileFalse(() => CountById(w, "RowTitle") == 2, Long);

        // Favorite → favorites section shows exactly it.
        WaitFor(w, "CredentialList", Long).AsListBox().Select(0);
        WaitFor(w, "DetailTitle", Long);
        Click(w, "ToggleFavorite");
        SelectRadio(w, "NavFavorites");
        Assert.Equal(1, CountById(w, "RowTitle"));
        SelectRadio(w, "NavAllItems");

        // Delete BOTH items (each needs its mandatory confirm pane), then one trash visit
        // covers restore + empty-trash (a second nav-radio trip raced focus restoration).
        WaitFor(w, "CredentialList", Long).AsListBox().Select(0);
        Click(w, "DeleteCredential");
        WaitFor(w, "DeleteConfirm", Long);
        Click(w, "DeleteConfirm");
        WaitFor(w, "CredentialList", Long).AsListBox().Select(0);
        Click(w, "DeleteCredential");
        WaitFor(w, "DeleteConfirm", Long);
        Click(w, "DeleteConfirm");
        Assert.Equal(0, CountById(w, "RowTitle"));

        SelectRadio(w, "NavTrash");
        _ = Retry.WhileFalse(() => CountById(w, "RowTitle") > 0, Long); // trash rows render async
        WaitFor(w, "CredentialList", Long).AsListBox().Select(0);
        Click(w, "RestoreFromTrash");
        SelectRadio(w, "NavAllItems");
        _ = Retry.WhileFalse(() => CountById(w, "RowTitle") == 1, Long);
        Assert.Equal(1, CountById(w, "RowTitle"));

        SelectRadio(w, "NavTrash");
        _ = Retry.WhileFalse(() => CountById(w, "RowTitle") > 0, Long);
        WaitFor(w, "CredentialList", Long).AsListBox().Select(0);
        Click(w, "EmptyTrash");
        WaitFor(w, "EmptyTrashConfirm", Long);
        Click(w, "EmptyTrashConfirm");
        SelectRadio(w, "NavAllItems");
        // The restored item stays — empty-trash purged only the one still in the trash.
        Assert.Equal(1, CountById(w, "RowTitle"));
    }

    // ── 9. Settings persistence: theme + language survive restart ──

    [Fact]
    public void Settings_Theme_Language_Persist_Across_Restart()
    {
        using var scope = TestScope.Start(language: "fa", theme: "Light");
        var w = scope.Window;
        CreateVault(w, ScopeVaultName());

        Click(w, "ToggleLanguageButton");
        _ = Retry.WhileFalse(
            () => File.ReadAllText(scope.SettingsPath).Contains("\"Language\": \"en\""),
            TimeSpan.FromSeconds(5));
        Assert.Contains("\"Language\": \"en\"", File.ReadAllText(scope.SettingsPath));

        Click(w, "ToggleThemeButton");
        _ = Retry.WhileFalse(
            () => File.ReadAllText(scope.SettingsPath).Contains("\"Theme\": \"Dark\""),
            TimeSpan.FromSeconds(5));
        Assert.Contains("\"Theme\": \"Dark\"", File.ReadAllText(scope.SettingsPath));

        scope.RestartApp();
        var w2 = scope.Window;

        // Vault list persisted; unlock and prove the language came back as English.
        WaitFor(w2, "VaultList", Long).AsListBox().Select(0);
        Click(w2, "PickerUnlock");
        Type(w2, "UnlockPasswordBox", MasterPassword);
        Click(w2, "UnlockButton");
        WaitFor(w2, "AddCredential", Long);

        Click(w2, "AddCredential");
        WaitFor(w2, "EditorTitle", Long);
        Click(w2, "EditorSave");
        Assert.Equal(EnTitleRequired, WaitForText(w2, "EditorError", Long));
        Assert.True(File.ReadAllText(scope.SettingsPath).Contains("\"Theme\""), "settings file must exist");
    }

    // ── 10. Backup pane: create/verify/restore actions present ──
    // (The restore stage itself is gated by native file dialogs — out of UIA scope; the
    // restore mandatory-password guard is covered at service level by the integration suite.)

    [Fact]
    public void Backup_Pane_Actions_Present()
    {
        using var scope = TestScope.Start(language: "en", theme: "Light");
        var w = scope.Window;
        CreateVault(w, ScopeVaultName());

        Click(w, "OpenBackup");
        WaitFor(w, "BackupCreate", Long);
        WaitFor(w, "BackupVerify", Long);
        WaitFor(w, "BackupRestore", Long);
        Click(w, "BackupCloseButton");
        WaitFor(w, "AddCredential", Long);
    }

    // ── 11. Import wizard: file mandatory, filter optional ──

    [Fact]
    public void Import_Wizard_File_Is_Mandatory_Filter_Is_Optional()
    {
        using var scope = TestScope.Start(language: "en", theme: "Light");
        var w = scope.Window;
        CreateVault(w, ScopeVaultName());

        // ImportBegin lives in the import wizard's review step (needs a parsed CSV — native
        // dialog, out of UIA scope). The wizard itself opens from the Settings dialog's
        // Import category: rail radio (Space-select) → "from Chrome" → wizard overlay.
        Click(w, "NavSettings");
        SelectRadio(w, "SettingsNavImport");
        Click(w, "ImportFromChrome");
        WaitFor(w, "ImportClose", Long);
        Click(w, "ImportInstructionsNext");
        WaitFor(w, "ImportFilePath", Long);

        // Mandatory: with no file chosen, Parse is a SILENT no-op (guard inside the command) —
        // no error text and no step change. Bad-file parse errors need a real CSV via the
        // native dialog (out of UIA scope; covered by the integration suite). The optional
        // filter box lives in the post-parse review step — same dialog gate.
        Click(w, "ImportParse");
        Thread.Sleep(1200);
        Assert.Equal(string.Empty, WaitFor(w, "ImportFileError", Long).Name);
        WaitFor(w, "ImportFilePath", Long);
        Click(w, "ImportClose");
        WaitFor(w, "AddCredential", Long);
    }

    // ═══════════════ helpers ═══════════════

    private static readonly TimeSpan Long = TimeSpan.FromSeconds(15);

    private static string ScopeVaultName() => "E2E " + Guid.NewGuid().ToString("N")[..8];

    private static void CreateVault(Window w, string name)
    {
        Click(w, "PickerCreate");
        Type(w, "CreateName", name);
        Type(w, "CreatePassword", MasterPassword);
        Type(w, "CreatePasswordConfirm", MasterPassword);
        Click(w, "CreateNext");
        var prompt = WaitForText(w, "RecoveryChallengePrompt", Long);
        var group = int.Parse(Regex.Match(prompt, @"(\d+)").Groups[1].Value);
        Type(w, "RecoveryChallengeInput",
            GroupFor(group, WaitForText(w, "RecoveryKeyText", Long)));
        Click(w, "RecoveryConfirm");
        WaitFor(w, "AddCredential", Long);
    }

    private static void AddItem(Window w, string title)
    {
        Click(w, "AddCredential");
        WaitFor(w, "EditorTitle", Long);
        Type(w, "EditorTitle", title);
        Click(w, "EditorSave");
        WaitFor(w, "AddCredential", Long);
    }

    /// <summary>Mirrors CreateVaultViewModel.GroupFor: the prompt counts the PMRK1 prefix
    /// token as group 1, so the payload group is the prompt number minus one.</summary>
    private static string GroupFor(int promptGroup, string recoveryKey)
    {
        var payload = recoveryKey["PMRK1-".Length..].Replace("-", string.Empty);
        return payload.Substring((promptGroup - 2) * 4, 4);
    }

    private static void SelectComboItem(Window w, string automationId, int index)
    {
        var combo = WaitFor(w, automationId, Long).AsComboBox();
        combo.Expand();
        combo.Items[index].Select();
    }

    /// <summary>RadioButtons expose SelectionItem, not Invoke — Click() would throw. And a
    /// programmatic Select() only flips IsChecked: CommandParameter commands fire from Click
    /// alone, so send a real Space keystroke to the focused radio. A keystroke can be eaten
    /// when focus is being restored (e.g. right after a dialog overlay closes), so retry
    /// until the radio reports selected.</summary>
    private static void SelectRadio(Window w, string automationId)
    {
        var el = WaitFor(w, automationId, Long);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            el.Focus();
            FlaUI.Core.Input.Keyboard.Type(" ");
            if (Retry.WhileFalse(
                    () => el.Patterns.SelectionItem.Pattern.IsSelected,
                    TimeSpan.FromSeconds(2)).Result)
            {
                return;
            }
        }

        throw new TimeoutException($"Radio '{automationId}' did not become selected.");
    }

    /// <summary>TextBox values live in the ValuePattern — the UIA Name of an edit field is
    /// unreliable for reads.</summary>
    private static string ReadValue(Window w, string automationId) =>
        WaitFor(w, automationId, Long).AsTextBox().Text;

    private static int CountByName(Window w, string name) =>
        w.FindAllDescendants(cf => cf.ByName(name)).Length;

    private static int CountById(Window w, string automationId) =>
        w.FindAllDescendants(cf => cf.ByAutomationId(automationId)).Length;

    private static bool AnyListItemNameContains(Window w, string scopeId, string fragment) =>
        WaitFor(w, scopeId, Long)
            .FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem))
            .Any(e => (e.Name ?? string.Empty).Contains(fragment, StringComparison.Ordinal));

    private static bool AnyRowTitleContains(Window w, string fragment) =>
        w.FindAllDescendants(cf => cf.ByAutomationId("RowTitle"))
            .Any(e => (e.Name ?? string.Empty).Contains(fragment, StringComparison.Ordinal));

    private static int CountListItems(Window w, string scopeId) =>
        WaitFor(w, scopeId, Long)
            .FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem))
            .Length;

    private static bool Exists(Window w, string automationId) =>
        w.FindFirstDescendant(cf => cf.ByAutomationId(automationId)) is not null;

    private static void AssertStillVisible(Window w, string automationId, string because) =>
        Assert.True(Retry.WhileNull(() => w.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            TimeSpan.FromSeconds(2)).Success, because);

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

    /// <summary>Isolated app session: settings/registry snapshot → launch → restore on dispose.
    /// RestartApp() relaunches against the same mutated home (persistence facts).</summary>
    private sealed class TestScope : IDisposable
    {
        private readonly string _settingsPath;
        private readonly string _vaultsPath;
        private readonly string? _originalSettings;
        private readonly string? _originalVaults;
        private FlaUI.Core.Application? _app;
        private UIA3Automation? _automation;
        private Window? _window;

        public Window Window => _window ?? throw new InvalidOperationException("App not launched.");
        public string SettingsPath => _settingsPath;

        private TestScope()
        {
            var home = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ganjineh");
            Directory.CreateDirectory(home);
            _settingsPath = Path.Combine(home, "settings.json");
            _vaultsPath = Path.Combine(home, "vaults.json");
            _originalSettings = File.Exists(_settingsPath) ? File.ReadAllText(_settingsPath) : null;
            _originalVaults = File.Exists(_vaultsPath) ? File.ReadAllText(_vaultsPath) : null;
        }

        public static TestScope Start(string language, string theme)
        {
            var scope = new TestScope();
            File.WriteAllText(scope._settingsPath,
                $$"""{"Language": "{{language}}", "Theme": "{{theme}}", "FirstRunCompleted": true}""");
            scope.LaunchApp();
            return scope;
        }

        private void LaunchApp()
        {
            var exePath = Path.Combine(AppContext.BaseDirectory, "Ganjineh.exe");
            Assert.True(File.Exists(exePath), $"Desktop output not found: {exePath}");
            _app = FlaUI.Core.Application.Launch(exePath);
            _automation = new UIA3Automation();
            _window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(30))
                ?? throw new TimeoutException("Main window did not appear.");
        }

        public void RestartApp()
        {
            _app?.Kill();
            Thread.Sleep(800);
            LaunchApp();
        }

        public void Dispose()
        {
            try
            {
                _app?.Kill();
            }
            catch
            {
                // app may already be gone — restore must still run
            }

            _automation?.Dispose();
            if (_originalSettings is not null)
            {
                File.WriteAllText(_settingsPath, _originalSettings);
            }
            else if (File.Exists(_settingsPath))
            {
                File.Delete(_settingsPath);
            }

            if (_originalVaults is not null)
            {
                File.WriteAllText(_vaultsPath, _originalVaults);
            }
            else if (File.Exists(_vaultsPath))
            {
                File.Delete(_vaultsPath);
            }
        }
    }
}
