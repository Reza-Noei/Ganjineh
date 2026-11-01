using System.IO;
using System.Reflection;
using System.Text;
using Ganjineh.Application.Abstractions;
using Ganjineh.Application.Import;
using Ganjineh.Application.Items;
using Ganjineh.Application.Vaults;
using Ganjineh.Desktop.ViewModels;
using Ganjineh.Infrastructure.Localization;
using Ganjineh.Persistence.Items;
using Ganjineh.Persistence.Registry;
using Xunit;

namespace Ganjineh.Desktop.UITests;

/// <summary>
/// The complete import-wizard journey driven through the real view-model (the exact object the
/// XAML binds) against a real SQLCipher vault: instructions → file → parse → preview states →
/// conflict actions → execution → results → navigation event → session-loss teardown. This is
/// the deterministic half of the Phase 9 E2E — this host's modal-dialog UIA provider cannot be
/// driven reliably, so the FlaUI test (Phase9UiTests) covers the shell up to the dialog.
/// Synthetic credentials only.
/// </summary>
public sealed class BrowserImportViewModelJourneyTests : IDisposable
{
    private const string MasterPassword = "journey-رمز-1!";

    private readonly string _root = Directory.CreateTempSubdirectory("ganjineh-p9vm").FullName;
    private readonly VaultSession _session;
    private readonly VaultItemService _items = new();
    private readonly LocalizationService _localizer = new();

    public BrowserImportViewModelJourneyTests()
    {
        _localizer.SetCulture("en");
        var lifecycle = new VaultLifecycleService(
            registryFactory: () => new VaultRegistry(Path.Combine(_root, "vaults.json")));
        var created = lifecycle.CreateAsync(new CreateVaultRequest
        {
            Directory = _root,
            MasterPassword = MasterPassword.ToCharArray(),
        }).GetAwaiter().GetResult();
        _session = created.Session;
    }

    private (BrowserImportViewModel Vm, VaultSessionManager Sessions) CreateVm()
    {
        var sessions = new VaultSessionManager();
        return (new BrowserImportViewModel(new BrowserImportService(), sessions, _localizer), sessions);
    }

    private string WriteCsv(string content)
    {
        var path = Path.Combine(_root, Path.GetRandomFileName() + ".csv");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
        return path;
    }

    private const string ChromeHeader = "name,url,username,password,note";

    private static string FirefoxHeader() =>
        "url,username,password,httpRealm,formActionOrigin,guid,timeCreated,timeLastUsed,timePasswordChanged";

    [Fact]
    public async Task Full_Journey_Parse_Preview_Conflicts_Import_Results_Navigation()
    {
        // An existing login the CSV will collide with (origin+username).
        var existing = await _items.CreateAsync(_session, new ItemDraft
        {
            Kind = ItemType.Login, Title = "بانک ملی",
            Data = new Dictionary<string, object?>
            {
                ["username"] = "ali@x.ir", ["password"] = "رمز-قدیمی",
                ["urls"] = new List<string> { "https://bank.ir" },
            },
        });
        var (vm, sessions) = CreateVm();
        sessions.Activate(_session);

        // ── step 0: entry preconfigures the browser and resets state ──
        vm.OpenBrowserCommand.Execute("chrome");
        Assert.True(vm.IsVisible);
        Assert.True(vm.IsInstructionsStep);
        Assert.Equal(ImportBrowser.Chrome, vm.Browser);
        Assert.Contains("Chrome", vm.WizardTitle, StringComparison.OrdinalIgnoreCase);

        // ── step 1 → 2: a malformed file aborts with a meaningful error ──
        vm.BeginFileSelectionCommand.Execute(null);
        Assert.True(vm.IsFileStep);
        vm.SetSelectedFile(WriteCsv(ChromeHeader + "\n" +
            "بد,https://bad.site,locked,bad,\"unterminated\n"));
        vm.ParseFileCommand.Execute(null);
        await WaitUntilAsync("parse", () => !vm.IsParsing);
        Assert.True(vm.IsFileStep);
        Assert.False(string.IsNullOrEmpty(vm.FileError));
        Assert.False(vm.IsPreviewStep);

        // ── a clean file parses: preview with duplicate flagged, default Skip ──
        vm.SetSelectedFile(WriteCsv(ChromeHeader + "\n" +
            "بانک ملی,https://bank.ir/login,ali@x.ir,رمز-تازه,\n" +
            "گیت‌هاب,https://github.com,gh-user,gh-pass,\n" +
            "خالی,,,\n"));
        vm.ParseFileCommand.Execute(null);
        await WaitUntilAsync("parse", () => !vm.IsParsing);
        Assert.True(vm.IsPreviewStep);
        Assert.Equal(2, vm.RowsView.Count);
        Assert.Contains("2", vm.CountsLine.Replace('۲', '2'));
        Assert.Null(vm.UnsupportedLine);

        var duplicateRow = vm.RowsView.Single(r => r.IsDuplicate);
        Assert.Equal("بانک ملی", duplicateRow.Title);
        Assert.Equal(0, duplicateRow.ConflictIndex);                       // default Skip (§3/§8)
        Assert.Equal(ImportRowAction.Skip, duplicateRow.EffectiveAction);
        Assert.Equal(ImportRowAction.Import, vm.RowsView.Single(r => !r.IsDuplicate).EffectiveAction);

        // The preview model carries no password material at all (§3).
        Assert.Null(typeof(ImportRowViewModel).GetProperty("Password"));
        Assert.All(vm.RowsView, r => Assert.DoesNotContain("رمز", r.Title + r.Username + r.OriginDisplay, StringComparison.Ordinal));

        // Filter + selection helpers operate on the bindable rows.
        vm.FilterText = "گیت";
        Assert.Single(vm.RowsView);
        vm.FilterText = string.Empty;
        Assert.Equal(2, vm.RowsView.Count);
        vm.DeselectAllCommand.Execute(null);
        Assert.All(vm.RowsView, r => Assert.False(r.IsSelected));
        vm.SelectAllCommand.Execute(null);
        Assert.All(vm.RowsView, r => Assert.True(r.IsSelected));

        // ── step 2 → 3: execute with the duplicate set to Update Existing ──
        // (filter changes rebuild the row VMs — resolve the live instance, not the earlier one)
        duplicateRow = vm.RowsView.Single(r => r.IsDuplicate);
        duplicateRow.ConflictIndex = 1;
        Assert.Equal(ImportRowAction.UpdateExisting, duplicateRow.EffectiveAction);
        Guid? navigatedItem = null;
        vm.OpenItemRequested += (_, id) => navigatedItem = id;
        vm.BeginImportCommand.Execute(null);
        Assert.True(vm.IsProgressStep);
        await WaitUntilAsync("import-done", () => !vm.IsImporting);

        Assert.False(string.IsNullOrEmpty(vm.ResultSummary));
        Assert.Contains("Updated: 1", vm.ResultSummary, StringComparison.Ordinal);
        Assert.Contains("Imported: 1", vm.ResultSummary, StringComparison.Ordinal);
        Assert.True(vm.HasWrittenItems);
        Assert.True(vm.CanOpenSourceFolder);

        // The vault shows the update (refreshed password) and the fresh import.
        var logins = await _items.ListAsync(_session, new ItemFilter(Kind: ItemType.Login));
        Assert.Equal(2, logins.Count);
        var updated = await _items.GetAsync(_session, existing.Id);
        Assert.Equal("رمز-تازه", updated.Data["password"]?.ToString());
        var history = await _items.GetHistoryAsync(_session, existing.Id);
        Assert.True(history.Count >= 2, "Update must append a version (history preserved).");

        // ── navigation: the shell is told to open the first written item ──
        vm.ShowImportedItemCommand.Execute(null);
        Assert.False(vm.IsVisible);                                       // wizard closes itself
        Assert.Equal(updated.Id, navigatedItem);                          // first written, in row order

        vm.Dispose();
    }

    [Fact]
    public async Task Vault_Loss_Closes_Wizard_And_Blocks_Further_Parse()
    {
        var (vm, sessions) = CreateVm();
        sessions.Activate(_session);
        vm.OpenBrowserCommand.Execute("firefox");
        vm.BeginFileSelectionCommand.Execute(null);
        // Firefox schema: url first (no title column); six optional columns empty.
        var rows = string.Join("\n", Enumerable.Range(1, 40).Select(i => $"https://{i}.com,u{i},p{i},,,,,,"));
        vm.SetSelectedFile(WriteCsv(FirefoxHeader() + "\n" + rows + "\n"));
        vm.ParseFileCommand.Execute(null);
        await WaitUntilAsync("parse", () => !vm.IsParsing);
        Assert.True(vm.IsPreviewStep);
        Assert.Equal(40, vm.RowsView.Count(r => r.Status == ImportRecordStatus.Ready && r.IsSelected));

        // Run completes against the live session (mid-flight lock semantics — StaleSession →
        // stopped-early with the prefix kept — are covered deterministically in
        // IntegrationTests.Phase9Tests; the dispatcher serializes records against the lock).
        vm.BeginImportCommand.Execute(null);
        Assert.True(vm.IsProgressStep, $"BeginImport did not start: step={vm.Step} rows={vm.RowsView.Count}");
        await WaitUntilAsync("import-done", () => !vm.IsImporting);
        Assert.Contains("Imported: 40", vm.ResultSummary, StringComparison.Ordinal);
        Assert.True(sessions.Current is not null,
            $"current lost after import: visible={vm.IsVisible} step={vm.Step} state={_session.State}");

        // Locking (the app's real lock path) closes the wizard via SessionChanged…
        sessions.LockCurrent();
        Assert.False(vm.IsVisible, "wizard must close on session change");

        // … and the dead session cannot parse anything more.
        vm.OpenBrowserCommand.Execute("chrome");
        vm.BeginFileSelectionCommand.Execute(null);
        vm.SetSelectedFile(WriteCsv(ChromeHeader + "\nx,https://x.com,u,p,\n"));
        vm.ParseFileCommand.Execute(null);
        await WaitUntilAsync("parse-after-lock", () => !vm.IsParsing);
        Assert.False(string.IsNullOrEmpty(vm.FileError), $"session-lost error missing: '{vm.FileError}'");
        Assert.False(vm.IsPreviewStep, $"dead session parsed anyway: step={vm.Step} error='{vm.FileError}'");
        vm.Dispose();
    }

    [Fact]
    public async Task Wrong_Format_File_Produces_Meaningful_Error_Without_Content()
    {
        var (vm, sessions) = CreateVm();
        sessions.Activate(_session);
        vm.OpenBrowserCommand.Execute("chrome");
        vm.BeginFileSelectionCommand.Execute(null);
        vm.SetSelectedFile(WriteCsv("colA,colB\n1,2\n"));
        vm.ParseFileCommand.Execute(null);
        await WaitUntilAsync("parse", () => !vm.IsParsing);

        Assert.False(string.IsNullOrEmpty(vm.FileError));
        Assert.DoesNotContain("1,2", vm.FileError, StringComparison.Ordinal); // never raw file content
        Assert.True(vm.IsVisible);                                        // wizard stays open on step 1
        vm.Dispose();
    }

    private static async Task WaitUntilAsync(string stage, Func<bool> condition, int timeoutMs = 60_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"Stage '{stage}' did not complete within {timeoutMs} ms.");
    }

    public void Dispose()
    {
        try { _session.Dispose(); }
        catch (InvalidOperationException) { /* already locked by a test */ }
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
