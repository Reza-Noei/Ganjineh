using System.Text;
using PasswordManager.Application.Import;
using PasswordManager.Application.Items;
using PasswordManager.Application.Vaults;
using PasswordManager.Persistence.Items;
using PasswordManager.Persistence.Registry;
using Xunit;

namespace PasswordManager.IntegrationTests;

/// <summary>
/// Phase 9: browser CSV import end-to-end on real SQLCipher vaults — parsing against a live
/// session, duplicate detection/conflict resolution, provenance, per-record transaction
/// semantics, cancellation, and persistence across lock/unlock. The lock-mid-run code path is
/// the same stale-session guard verified here at the entry points; the wizard additionally
/// closes on SessionChanged (UI tests). Synthetic credentials only.
/// </summary>
public sealed class Phase9Tests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ganjineh-p9").FullName;

    private VaultLifecycleService Lifecycle() =>
        new(registryFactory: () => new VaultRegistry(Path.Combine(_root, "vaults.json")));

    private (BrowserImportService Import, VaultItemService Items) Services() =>
        (new BrowserImportService(), new VaultItemService());

    private async Task<(VaultLifecycleService Lifecycle, VaultSession Session)> CreateVaultAsync(string password)
    {
        var lifecycle = Lifecycle();
        var created = await lifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = password.ToCharArray() });
        return (lifecycle, created.Session);
    }

    private string WriteCsv(string content, bool bom = false)
    {
        var path = Path.Combine(_root, Path.GetRandomFileName() + ".csv");
        var bytes = Encoding.UTF8.GetBytes(content);
        if (bom)
        {
            bytes = [0xEF, 0xBB, 0xBF, .. bytes];
        }

        File.WriteAllBytes(path, bytes);
        return path;
    }

    private const string ChromeHeader = "name,url,username,password,note";

    private const string FirefoxHeader =
        "url,username,password,httpRealm,formActionOrigin,guid,timeCreated,timeLastUsed,timePasswordChanged";

    // ── import round-trips ──

    [Fact]
    public async Task Chrome_Import_RoundTrip_Persists_Encrypted_With_Provenance()
    {
        var (lifecycle, session) = await CreateVaultAsync("p9-chrome-رمز!");
        var (import, items) = Services();
        var csv = ChromeHeader + "\n" +
                  "بانک ملی,https://bank.ir/login,ali@x.ir,رمز-قوی-۹۹,یادداشت شخصی\n" +
                  "گیت‌هاب,https://github.com,gh-user,gh-pass-123,\n";
        var path = WriteCsv(csv);

        var parsed = import.Parse(session, ImportBrowser.Chrome, path);
        Assert.Equal(2, parsed.Rows.Count);
        var plans = parsed.Rows.Select(r => new ImportRowPlan(r.RowNumber, ImportRowAction.Import, r.ExistingItemId)).ToList();
        var result = await import.ExecuteAsync(session, parsed, plans);

        Assert.Equal(0, result.Failed);
        Assert.False(result.StoppedEarly);
        Assert.Equal(2, result.Imported);

        var logins = await items.ListAsync(session, new ItemFilter(Kind: ItemType.Login));
        Assert.Equal(2, logins.Count);

        var bank = await items.GetAsync(session, logins.First(l => l.Title == "بانک ملی").Id);
        Assert.Equal("ali@x.ir", bank.Data["username"]?.ToString());
        Assert.Equal("رمز-قوی-۹۹", bank.Data["password"]?.ToString());
        Assert.Equal("یادداشت شخصی", bank.Description);
        Assert.Equal("https://bank.ir/login", Assert.IsType<List<string>>(bank.Data["urls"])[0]);
        // Provenance rides inside the encrypted payload (§12); the source path never does.
        Assert.Equal("chrome", bank.Data[ImportProvenance.SourceBrowser]);
        Assert.NotNull(bank.Data[ImportProvenance.ImportedAt]);
        Assert.Equal("2", bank.Data[ImportProvenance.SourceRecordId]);
        Assert.All(bank.Data.Values, v => Assert.DoesNotContain(path, v?.ToString(), StringComparison.Ordinal));

        // Ciphertext at rest leaks nothing (checked after Lock releases the file).
        var vaultDir = session.VaultDirectory;
        var dbPath = Path.Combine(vaultDir, "vault.db");
        session.Lock();
        var ciphertextAtRest = await File.ReadAllBytesAsync(dbPath);
        Assert.DoesNotContain(Encoding.UTF8.GetBytes("رمز-قوی-۹۹"), ciphertextAtRest);
        Assert.DoesNotContain(Encoding.UTF8.GetBytes("ali@x.ir"), ciphertextAtRest);

        // Persistence across lock/unlock through the real SQLCipher database.
        using var reopened = await lifecycle.UnlockWithPasswordAsync(vaultDir, "p9-chrome-رمز!".ToCharArray());
        var reopenedLogins = await items.ListAsync(reopened, new ItemFilter(Kind: ItemType.Login));
        Assert.Equal(2, reopenedLogins.Count);
        var reopenedBank = await items.GetAsync(reopened, reopenedLogins.First(l => l.Title == "بانک ملی").Id);
        Assert.Equal("رمز-قوی-۹۹", reopenedBank.Data["password"]?.ToString());
    }

    [Fact]
    public async Task Firefox_Import_Preserves_Guid_And_PasswordChanged_Metadata()
    {
        var (_, session) = await CreateVaultAsync("p9-ff-رمز!");
        var (import, items) = Services();
        var csv = FirefoxHeader + "\n" +
                  "https://mail.ir/,کاربر-۱,رمز-۱,,,guid-1234,,,9876543210987654\n";
        var path = WriteCsv(csv);

        var parsed = import.Parse(session, ImportBrowser.Firefox, path);
        var row = Assert.Single(parsed.Rows);
        Assert.False(row.IsDuplicate);
        var result = await import.ExecuteAsync(session, parsed,
            [new ImportRowPlan(row.RowNumber, ImportRowAction.Import, null)]);

        Assert.Equal(1, result.Imported);
        var login = Assert.Single(await items.ListAsync(session, new ItemFilter(Kind: ItemType.Login)));
        var detail = await items.GetAsync(session, login.Id);
        Assert.Equal("guid-1234", detail.Data[ImportProvenance.SourceRecordId]);
        // PRTime microseconds → Unix seconds.
        Assert.Equal("9876543210", detail.Data["sourceTimePasswordChanged"]);
        Assert.Equal("firefox", detail.Data[ImportProvenance.SourceBrowser]);
        Assert.Equal("mail.ir", detail.Title);
    }

    // ── duplicate detection & conflict resolution ──

    [Fact]
    public async Task Duplicate_Default_Skip_Never_Overwrites()
    {
        var (_, session) = await CreateVaultAsync("p9-dup-رمز!");
        var (import, items) = Services();
        await items.CreateAsync(session, new ItemDraft
        {
            Kind = ItemType.Login,
            Title = "بانک ملی",
            Description = "یادداشت اصلی",
            Data = new Dictionary<string, object?>
            {
                ["username"] = "ali@x.ir",
                ["password"] = "رمز-اصلی",
                ["urls"] = new List<string> { "https://bank.ir/portal" },
                ["totpSecret"] = "JBSWY3DPEHPK3PXP",
            },
        });

        var csv = ChromeHeader + "\nبانک ملی جدید,https://bank.ir/other,ali@x.ir,رمز-جدید,\n";
        var parsed = import.Parse(session, ImportBrowser.Chrome, WriteCsv(csv));
        var row = Assert.Single(parsed.Rows);
        // Path differs, origin+username match → duplicate (§8).
        Assert.True(row.IsDuplicate);
        Assert.Equal("بانک ملی", row.DuplicateOfTitle);
        Assert.NotNull(row.ExistingItemId);

        var result = await import.ExecuteAsync(session, parsed,
            [new ImportRowPlan(row.RowNumber, ImportRowAction.Skip, row.ExistingItemId)]);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Imported + result.Updated);

        var original = await items.GetAsync(session, row.ExistingItemId!.Value);
        Assert.Equal("رمز-اصلی", original.Data["password"]?.ToString());
        Assert.Equal("یادداشت اصلی", original.Description);
        Assert.Single(await items.ListAsync(session, new ItemFilter(Kind: ItemType.Login)));
    }

    [Fact]
    public async Task Update_Existing_Merges_Preserves_Metadata_And_History()
    {
        var (_, session) = await CreateVaultAsync("p9-update-رمز!");
        var (import, items) = Services();
        var created = await items.CreateAsync(session, new ItemDraft
        {
            Kind = ItemType.Login,
            Title = "بانک ملی",
            Description = "یادداشت کاربر",
            Data = new Dictionary<string, object?>
            {
                ["username"] = "ali@x.ir",
                ["password"] = "رمز-قدیمی",
                ["urls"] = new List<string> { "https://bank.ir" },
                ["totpSecret"] = "JBSWY3DPEHPK3PXP",
            },
            CustomFields = [new CustomField("f1", "شناسه", CustomFieldType.Text, "۱۲۳", 0, false)],
        });
        await items.ToggleFavoriteAsync(session, created.Id);

        var csv = ChromeHeader + "\nبانک ملی,https://bank.ir/login,ali@x.ir,رمز-تازه,\n";
        var parsed = import.Parse(session, ImportBrowser.Chrome, WriteCsv(csv));
        var row = Assert.Single(parsed.Rows);
        Assert.True(row.IsDuplicate);

        var result = await import.ExecuteAsync(session, parsed,
            [new ImportRowPlan(row.RowNumber, ImportRowAction.UpdateExisting, row.ExistingItemId)]);
        Assert.Equal(1, result.Updated);

        // Same item identity; password refreshed; everything unrelated preserved (§8).
        var updated = await items.GetAsync(session, created.Id);
        Assert.Equal("رمز-تازه", updated.Data["password"]?.ToString());
        Assert.Equal("JBSWY3DPEHPK3PXP", updated.Data["totpSecret"]?.ToString());
        Assert.Equal("۱۲۳", updated.CustomFields.Single(f => f.Label == "شناسه").Value);
        Assert.Equal("یادداشت کاربر", updated.Description);
        Assert.Contains("https://bank.ir/login", Assert.IsType<List<string>>(updated.Data["urls"]));
        Assert.Equal("chrome", updated.Data[ImportProvenance.SourceBrowser]);
        Assert.True(updated.Favorite, "favorite flag survives the update");

        // The update appended a version — item history is intact.
        var history = await items.GetHistoryAsync(session, created.Id);
        Assert.True(history.Count >= 2, "Update must append a version (history preserved).");
        Assert.Single(await items.ListAsync(session, new ItemFilter(Kind: ItemType.Login)));
    }

    [Fact]
    public async Task Update_With_Empty_Csv_Password_Keeps_Stored_Password()
    {
        var (_, session) = await CreateVaultAsync("p9-empty-رمز!");
        var (import, items) = Services();
        var created = await items.CreateAsync(session, new ItemDraft
        {
            Kind = ItemType.Login, Title = "سایت",
            Data = new Dictionary<string, object?>
            {
                ["username"] = "u@x.com", ["password"] = "رمز-محفوظ",
                ["urls"] = new List<string> { "https://s.com" },
            },
        });

        var csv = ChromeHeader + "\nسایت,https://s.com,u@x.com,,\n";
        var parsed = import.Parse(session, ImportBrowser.Chrome, WriteCsv(csv));
        var row = Assert.Single(parsed.Rows);
        var result = await import.ExecuteAsync(session, parsed,
            [new ImportRowPlan(row.RowNumber, ImportRowAction.UpdateExisting, row.ExistingItemId)]);
        Assert.Equal(1, result.Updated);

        var updated = await items.GetAsync(session, created.Id);
        Assert.Equal("رمز-محفوظ", updated.Data["password"]?.ToString());
    }

    [Fact]
    public async Task Create_Copy_Second_Item_Appears()
    {
        var (_, session) = await CreateVaultAsync("p9-copy-رمز!");
        var (import, items) = Services();
        await items.CreateAsync(session, new ItemDraft
        {
            Kind = ItemType.Login, Title = "قدیمی",
            Data = new Dictionary<string, object?>
            {
                ["username"] = "u@x.com", ["password"] = "p1",
                ["urls"] = new List<string> { "https://s.com" },
            },
        });

        var csv = ChromeHeader + "\nجدید,https://s.com,u@x.com,p2,\n";
        var parsed = import.Parse(session, ImportBrowser.Chrome, WriteCsv(csv));
        var row = Assert.Single(parsed.Rows);
        Assert.True(row.IsDuplicate);
        var result = await import.ExecuteAsync(session, parsed,
            [new ImportRowPlan(row.RowNumber, ImportRowAction.CreateCopy, row.ExistingItemId)]);
        Assert.Equal(1, result.Imported);
        Assert.Equal(2, (await items.ListAsync(session, new ItemFilter(Kind: ItemType.Login))).Count);
    }

    [Fact]
    public async Task Non_Duplicate_Cases_Are_Not_Flagged()
    {
        var (_, session) = await CreateVaultAsync("p9-nondup-رمز!");
        var (import, items) = Services();
        await items.CreateAsync(session, new ItemDraft
        {
            Kind = ItemType.Login, Title = "acc1",
            Data = new Dictionary<string, object?>
            {
                ["username"] = "a@x.com", ["password"] = "p",
                ["urls"] = new List<string> { "https://example.com" },
            },
        });

        var csv = ChromeHeader + "\n" +
                  "acc2,https://example.com,b@x.com,p,\n" +
                  "mail,https://mail.example.com,a@x.com,p,\n" +
                  "http,http://example.com,a@x.com,p,\n" +
                  "www,https://www.example.com,a@x.com,p,\n";
        var parsed = import.Parse(session, ImportBrowser.Chrome, WriteCsv(csv));
        Assert.False(parsed.Rows[0].IsDuplicate, "different username on same origin is a different account");
        Assert.False(parsed.Rows[1].IsDuplicate, "subdomains are distinct");
        Assert.False(parsed.Rows[2].IsDuplicate, "http vs https are distinct auth contexts");
        Assert.True(parsed.Rows[3].IsDuplicate, "www. is folded to the bare domain");
    }

    [Fact]
    public async Task Import_Does_Not_Flag_Against_Another_Vault()
    {
        var (_, vaultA) = await CreateVaultAsync("p9-iso-A-رمز!");
        var (_, vaultB) = await CreateVaultAsync("p9-iso-B-رمز!");
        var (import, itemsA) = Services();
        var itemsB = new VaultItemService();

        await itemsA.CreateAsync(vaultA, new ItemDraft
        {
            Kind = ItemType.Login, Title = "A-item",
            Data = new Dictionary<string, object?>
            {
                ["username"] = "a@x.com", ["password"] = "pA",
                ["urls"] = new List<string> { "https://shared.com" },
            },
        });

        var csv = ChromeHeader + "\nB-item,https://shared.com,a@x.com,pB,\n";
        var parsed = import.Parse(vaultB, ImportBrowser.Chrome, WriteCsv(csv));
        // Vault B holds no such login: no duplicate, despite vault A having one (§8/§9).
        Assert.False(Assert.Single(parsed.Rows).IsDuplicate);
        var result = await import.ExecuteAsync(vaultB, parsed,
            [new ImportRowPlan(parsed.Rows[0].RowNumber, ImportRowAction.Import, null)]);
        Assert.Equal(1, result.Imported);
        Assert.Single(await itemsB.ListAsync(vaultB, new ItemFilter(Kind: ItemType.Login)));
        Assert.Single(await itemsA.ListAsync(vaultA, new ItemFilter(Kind: ItemType.Login)));
    }

    // ── execution semantics ──

    [Fact]
    public async Task Per_Record_Failure_Keeps_Others_And_Reports_Exact_Row()
    {
        var (_, session) = await CreateVaultAsync("p9-partial-رمز!");
        var (import, items) = Services();

        // Row 4 parses as "invalid" (oversized title) — forced into the plan to prove the
        // write-time ValidationException fails exactly that record and keeps the rest.
        var csv = ChromeHeader + "\n" +
                  "ok1,https://1.com,u1,p1,\n" +
                  "ok2,https://2.com,u2,p2,\n" +
                  new string('ع', 250) + ",https://3.com,u3,p3,\n";
        var parsed = import.Parse(session, ImportBrowser.Chrome, WriteCsv(csv));
        Assert.Equal(ImportRecordStatus.Invalid, parsed.Rows.Single(r => r.RowNumber == 4).Status);

        var plans = new List<ImportRowPlan>
        {
            new(2, ImportRowAction.Import, null),
            new(4, ImportRowAction.Import, null),
            new(3, ImportRowAction.Import, null),
        };
        var result = await import.ExecuteAsync(session, parsed, plans);
        Assert.Equal(2, result.Imported);
        Assert.Equal(1, result.Failed);
        Assert.Equal("Validation.TitleTooLong", result.FailureReasons[4]);
        Assert.Equal(2, (await items.ListAsync(session, new ItemFilter(Kind: ItemType.Login))).Count);
    }

    [Fact]
    public async Task Cancellation_Stops_With_Accurate_Prefix_Counts()
    {
        var (_, session) = await CreateVaultAsync("p9-cancel-رمز!");
        var (import, items) = Services();
        var rows = string.Join("\n", Enumerable.Range(1, 50).Select(i => $"s{i},https://{i}.com,u{i},p{i},"));
        var parsed = import.Parse(session, ImportBrowser.Chrome, WriteCsv(ChromeHeader + "\n" + rows + "\n"));
        var plans = parsed.Rows.Select(r => new ImportRowPlan(r.RowNumber, ImportRowAction.Import, null)).ToList();

        using var cts = new CancellationTokenSource();
        var cancelledOnce = false;
        var progress = new Progress<ImportProgress>(_ =>
        {
            if (!cancelledOnce)
            {
                cancelledOnce = true;
                cts.Cancel();
            }
        });

        var result = await import.ExecuteAsync(session, parsed, plans, progress, cts.Token);
        Assert.True(result.StoppedEarly);
        Assert.True(result.Imported < plans.Count);
        // The written prefix matches the reported counts exactly — nothing more, nothing less.
        Assert.Equal(result.Imported, (await items.ListAsync(session, new ItemFilter(Kind: ItemType.Login))).Count);
    }

    [Fact]
    public async Task Locked_Session_Rejects_Parse_And_Execute()
    {
        var (_, session) = await CreateVaultAsync("p9-locked-رمز!");
        var (import, _) = Services();
        var path = WriteCsv(ChromeHeader + "\ns,https://s.com,u,p,\n");
        session.Lock();
        Assert.Throws<StaleSessionException>(() => import.Parse(session, ImportBrowser.Chrome, path));
        await Assert.ThrowsAsync<StaleSessionException>(() =>
            import.ExecuteAsync(session, new BrowserImportSession
            {
                Browser = ImportBrowser.Chrome,
                SourceFilePath = path,
                UnsupportedColumns = [],
            }, []));
    }

    [Fact]
    public async Task Wrong_Browser_Entry_Is_Reported_Not_Parsed()
    {
        var (_, session) = await CreateVaultAsync("p9-wrong-رمز!");
        var (import, _) = Services();
        var csv = FirefoxHeader + "\nhttps://x.com,u,p,,,,,,,,\n";
        var ex = Assert.Throws<ImportSchemaException>(() =>
            import.Parse(session, ImportBrowser.Chrome, WriteCsv(csv)));
        Assert.Equal("Import.ErrorWrongBrowser", ex.ErrorKey);
        Assert.Equal("Firefox", ex.Detail);
    }

    [Fact]
    public async Task Import_Never_Modifies_The_Source_File()
    {
        var (_, session) = await CreateVaultAsync("p9-src-رمز!");
        var (import, items) = Services();
        var csv = ChromeHeader + "\ns,https://s.com,u,p,\n";
        var path = WriteCsv(csv);
        var before = await File.ReadAllBytesAsync(path);

        var parsed = import.Parse(session, ImportBrowser.Chrome, path);
        await import.ExecuteAsync(session, parsed,
            [new ImportRowPlan(parsed.Rows[0].RowNumber, ImportRowAction.Import, null)]);
        parsed.Dispose();

        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Single(await items.ListAsync(session, new ItemFilter(Kind: ItemType.Login)));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
