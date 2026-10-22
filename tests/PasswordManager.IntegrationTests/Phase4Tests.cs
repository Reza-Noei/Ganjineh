using System.Text;
using PasswordManager.Application.Credentials;
using PasswordManager.Application.Items;
using PasswordManager.Application.Vaults;
using PasswordManager.Persistence.Items;
using PasswordManager.Persistence.Migrations;
using PasswordManager.Persistence.Registry;
using Xunit;

namespace PasswordManager.IntegrationTests;

/// <summary>Phase 4: item kinds, custom fields, organization, history, trash, attachments, migration.</summary>
public sealed class Phase4Tests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ganjineh-p4").FullName;

    private VaultLifecycleService Lifecycle() =>
        new(registryFactory: () => new VaultRegistry(Path.Combine(_root, "vaults.json")));

    private (VaultItemService Items, FolderService Folders, TagService Tags, AttachmentService Attachments) Services() =>
        (new VaultItemService(), new FolderService(), new TagService(), new AttachmentService());

    private async Task<VaultSession> VaultAsync(VaultLifecycleService lifecycle)
    {
        var created = await lifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = "p4-رمز-1!".ToCharArray() });
        return created.Session;
    }

    // ── Item kinds round-trip ──

    public static IEnumerable<object[]> KindData()
    {
        yield return new object[]
        {
            ItemType.Login, new Dictionary<string, object?> { ["username"] = "u@x.ir", ["password"] = "رمز-1", ["urls"] = new List<string> { "https://a.ir" } },
        };
        yield return new object[]
        {
            ItemType.SecureNote, new Dictionary<string, object?> { ["content"] = "متن یادداشت چندخطی\nخط دوم", ["category"] = "شخصی" },
        };
        yield return new object[]
        {
            ItemType.PaymentCard, new Dictionary<string, object?> { ["holder"] = "ALI REZAEI", ["number"] = "6037991212345678", ["expMonth"] = "7", ["expYear"] = "2029", ["cvv"] = "123", ["issuer"] = "ملت" },
        };
        yield return new object[]
        {
            ItemType.Identity, new Dictionary<string, object?> { ["fullName"] = "علی رضایی", ["dob"] = "1370-05-12", ["emails"] = new List<string> { "a@b.ir" }, ["phones"] = new List<string> { "0912…" }, ["idNumbers"] = " secret " },
        };
        yield return new object[]
        {
            ItemType.Wifi, new Dictionary<string, object?> { ["ssid"] = "HomeNet", ["security"] = "WPA3", ["password"] = "wifi-pass", ["hidden"] = "false" },
        };
        yield return new object[]
        {
            ItemType.ApiCredential, new Dictionary<string, object?> { ["service"] = "Stripe", ["apiKey"] = "sk_1", ["apiSecret"] = "sec", ["token"] = "tok", ["endpoint"] = "https://api.stripe.com", ["environment"] = "prod", ["expiresAt"] = "2027-01-01" },
        };
        yield return new object[]
        {
            ItemType.SoftwareLicense, new Dictionary<string, object?> { ["product"] = "Rider", ["key"] = "LIC-123", ["email"] = "me@x.ir", ["purchasedAt"] = "2025-01-01", ["vendorUrl"] = "https://jetbrains.com" },
        };
        yield return new object[]
        {
            ItemType.RecoveryCodes, new Dictionary<string, object?> { ["service"] = "GitHub", ["codes"] = new List<string> { "1111-2222", "3333-4444" } },
        };
        yield return new object[]
        {
            ItemType.Custom, new Dictionary<string, object?>(),
        };
    }

    [Theory]
    [MemberData(nameof(KindData))]
    public async Task Item_Kind_RoundTrips_With_Encryption(ItemType kind, Dictionary<string, object?> data)
    {
        var (items, _, _, _) = Services();
        using var session = await VaultAsync(Lifecycle());

        var created = await items.CreateAsync(session, new ItemDraft
        {
            Kind = kind, Title = "عنوان " + kind, Data = data,
            CustomFields = [new CustomField(Guid.NewGuid().ToString(), "Serial", CustomFieldType.Text, "SN-۱۲۳", 0, false)],
        });

        var detail = await items.GetAsync(session, created.Id);
        Assert.Equal(kind, detail.Kind);
        Assert.Equal("عنوان " + kind, detail.Title);
        Assert.Equal("SN-۱۲۳", detail.CustomFields.Single(f => f.Label == "Serial").Value);
        foreach (var (key, expected) in data)
        {
            var expectedText = expected is List<string> list ? string.Join("|", list) : expected?.ToString();
            if (expected is List<string> expectedList)
            {
                var actualList = detail.Data[key] as List<string> ?? [];
                Assert.Equal(expectedList, actualList);
            }
            else
            {
                Assert.Equal(expectedText, detail.Data[key]?.ToString());
            }
        }

        session.Lock();

        // Encrypted at rest: type-specific data must not appear in the raw DB bytes.
        // (Needles shorter than 8 bytes are skipped — they false-positive on random ciphertext.)
        var dbBytes = await File.ReadAllBytesAsync(Path.Combine(session.VaultDirectory, "vault.db"));
        foreach (var (_, value) in data)
        {
            var text = value is List<string> l ? string.Join("|", l) : value?.ToString();
            if (string.IsNullOrEmpty(text) || text.Length < 8 || text.Contains('…'))
            {
                continue;
            }

            Assert.Equal(-1, dbBytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(text)));
        }
    }

    // ── Custom fields ──

    [Fact]
    public async Task Custom_Fields_Validate_And_Preserve_Order()
    {
        var (items, _, _, _) = Services();
        using var session = await VaultAsync(Lifecycle());

        await Assert.ThrowsAsync<ValidationException>(() => items.CreateAsync(session, new ItemDraft
        {
            Kind = ItemType.Custom,
            Title = "x",
            CustomFields = [new CustomField(Guid.NewGuid().ToString(), "", CustomFieldType.Text, null, 0, false)],
        }));

        await Assert.ThrowsAsync<ValidationException>(() => items.CreateAsync(session, new ItemDraft
        {
            Kind = ItemType.Custom,
            Title = "x",
            CustomFields = [new CustomField(Guid.NewGuid().ToString(), "Site", CustomFieldType.Url, "not-a-url", 0, false)],
        }));

        var created = await items.CreateAsync(session, new ItemDraft
        {
            Kind = ItemType.Custom,
            Title = "custom",
            CustomFields =
            [
                new CustomField(Guid.NewGuid().ToString(), "Second", CustomFieldType.Text, "2", 1, false),
                new CustomField(Guid.NewGuid().ToString(), "First", CustomFieldType.Concealed, "secret-1", 0, true),
                new CustomField(Guid.NewGuid().ToString(), "Count", CustomFieldType.Number, "42", 2, false),
            ],
        });

        var detail = await items.GetAsync(session, created.Id);
        Assert.Equal(["First", "Second", "Count"], detail.CustomFields.Select(f => f.Label));
        Assert.True(detail.CustomFields.Single(f => f.Label == "First").Concealed);
    }

    // ── Folders ──

    [Fact]
    public async Task Folders_Nest_Prevent_Cycles_And_Reparent_On_Delete()
    {
        var (items, folders, _, _) = Services();
        using var session = await VaultAsync(Lifecycle());

        var work = await folders.CreateAsync(session, "کار", null);
        var sub = await folders.CreateAsync(session, "پروژه‌ها", work.Id);
        var item = await items.CreateAsync(session, new ItemDraft { Kind = ItemType.Login, Title = "in-sub", FolderId = sub.Id });

        // Moving "کار" under "پروژه‌ها" would create a cycle.
        await Assert.ThrowsAsync<ValidationException>(() => folders.MoveAsync(session, work.Id, sub.Id));

        // Deleting the parent re-parents children to the deleted folder's parent (here: root).
        var deepItem = await items.CreateAsync(session, new ItemDraft { Kind = ItemType.Login, Title = "in-work", FolderId = work.Id });
        await folders.DeleteAsync(session, work.Id, deleteItems: false);

        var foldersLeft = await folders.ListAsync(session);
        Assert.Null(foldersLeft.Single(f => f.Id == sub.Id).ParentId);
        var detailSub = await items.GetAsync(session, item.Id);
        Assert.Equal(sub.Id, detailSub.FolderId);
        var detailDeep = await items.GetAsync(session, deepItem.Id);
        Assert.Null(detailDeep.FolderId);
    }

    // ── Tags ──

    [Fact]
    public async Task Tags_Deduplicate_Count_And_Delete_Cascades()
    {
        var (items, _, tags, _) = Services();
        using var session = await VaultAsync(Lifecycle());

        var work = await tags.CreateAsync(session, "کاری");
        var duplicate = await tags.CreateAsync(session, " کاری ");  // same after normalization
        Assert.Equal(work.Id, duplicate.Id);

        var item = await items.CreateAsync(session, new ItemDraft
        {
            Kind = ItemType.Login, Title = "tagged", TagIds = [work.Id],
        });

        var listed = await tags.ListAsync(session);
        Assert.Equal(1, listed.Single(t => t.Id == work.Id).UsageCount);

        await tags.DeleteAsync(session, work.Id);
        var detail = await items.GetAsync(session, item.Id);
        Assert.Empty(detail.TagIds); // FK cascade removed the join row
        Assert.Empty(await tags.ListAsync(session));
    }

    // ── History ──

    [Fact]
    public async Task History_Appends_Restores_And_Prunes()
    {
        var items = new VaultItemService(historyRetentionPerItem: 3);
        using var session = await VaultAsync(Lifecycle());

        var item = await items.CreateAsync(session, new ItemDraft
        {
            Kind = ItemType.Login, Title = "v1", Data = new Dictionary<string, object?> { ["password"] = "old-رمز" },
        });

        for (var round = 2; round <= 6; round++)
        {
            await items.UpdateAsync(session, item.Id, new ItemDraft
            {
                Kind = ItemType.Login, Title = "v" + round,
                Data = new Dictionary<string, object?> { ["password"] = "pass-" + round },
            });
        }

        var history = await items.GetHistoryAsync(session, item.Id);
        Assert.Equal(3, history.Count); // retention cap
        Assert.Equal(6, history[0].Version); // newest first

        // Restore version 4 → appended as a NEW version (7); current data = v4 content.
        await items.RestoreVersionAsync(session, item.Id, 4);
        var detail = await items.GetAsync(session, item.Id);
        Assert.Equal("pass-4", detail.Data["password"]);
        Assert.Equal(3, (await items.GetHistoryAsync(session, item.Id)).Count);

        // Historical secrets stay encrypted at rest.
        session.Lock();
        var dbBytes = await File.ReadAllBytesAsync(Path.Combine(session.VaultDirectory, "vault.db"));
        Assert.Equal(-1, dbBytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes("old-رمز")));
        Assert.Equal(-1, dbBytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes("pass-4")));
    }

    // ── Trash ──

    [Fact]
    public async Task Trash_SoftDelete_Restore_Purge_And_Retention()
    {
        var (items, _, _, _) = Services();
        using var session = await VaultAsync(Lifecycle());

        var keep = await items.CreateAsync(session, new ItemDraft { Kind = ItemType.SecureNote, Title = "keep" });
        var gone = await items.CreateAsync(session, new ItemDraft { Kind = ItemType.SecureNote, Title = "gone" });

        await items.TrashAsync(session, gone.Id);
        var live = await items.ListAsync(session);
        Assert.Single(live); // trashed excluded from normal lists

        var trashed = await items.ListAsync(session, new ItemFilter(TrashedOnly: true));
        Assert.Single(trashed);

        await items.RestoreAsync(session, gone.Id);
        Assert.Equal(2, (await items.ListAsync(session)).Count);

        await items.TrashAsync(session, gone.Id);
        await items.PurgeAsync(session, gone.Id);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => items.GetAsync(session, gone.Id));
        _ = keep;
    }

    // ── Attachments ──

    [Fact]
    public async Task Attachment_RoundTrip_Streams_And_Encrypts()
    {
        var (items, _, _, attachments) = Services();
        using var session = await VaultAsync(Lifecycle());

        var sourcePath = Path.Combine(_root, "source-پیوست.bin");
        var payload = new byte[5 * 1024 * 1024 + 12345]; // spans multiple chunks + partial tail
        Random.Shared.NextBytes(payload);
        await File.WriteAllBytesAsync(sourcePath, payload);

        var item = await items.CreateAsync(session, new ItemDraft { Kind = ItemType.SecureNote, Title = "with-attachment" });
        var added = await attachments.AddAsync(session, item.Id, sourcePath);

        Assert.Equal(payload.Length, added.SizeBytes);
        var blobPath = Path.Combine(session.VaultDirectory, "attachments", added.Id + ".pmblob");
        Assert.True(File.Exists(blobPath));

        // Blob must not contain plaintext.
        var blobBytes = await File.ReadAllBytesAsync(blobPath);
        Assert.Equal(-1, blobBytes.AsSpan().IndexOf(payload[..4096]));

        var exportPath = Path.Combine(_root, "exported.bin");
        await attachments.ExportAsync(session, added.Id, exportPath);
        var exported = await File.ReadAllBytesAsync(exportPath);
        Assert.True(exported.AsSpan().SequenceEqual(payload));

        // Tamper detection: flip a ciphertext byte → export fails and removes the partial file.
        blobBytes[^20] ^= 0xFF;
        await File.WriteAllBytesAsync(blobPath, blobBytes);
        var tampered = Path.Combine(_root, "tampered.bin");
        await Assert.ThrowsAnyAsync<Exception>(() => attachments.ExportAsync(session, added.Id, tampered));
        Assert.False(File.Exists(tampered));

        await attachments.DeleteAsync(session, added.Id);
        Assert.False(File.Exists(blobPath));
        Assert.Empty(await attachments.ListAsync(session, item.Id));
    }

    [Fact]
    public async Task Attachment_Rejects_Oversize_And_Cleans_Orphans()
    {
        var (items, _, _, attachments) = Services();
        using var session = await VaultAsync(Lifecycle());
        var item = await items.CreateAsync(session, new ItemDraft { Kind = ItemType.SecureNote, Title = "att" });

        var huge = Path.Combine(_root, "huge.bin");
        await using (var stream = new FileStream(huge, FileMode.Create))
        {
            stream.SetLength(100L * 1024 * 1024 + 1);
        }

        await Assert.ThrowsAsync<ValidationException>(() => attachments.AddAsync(session, item.Id, huge));

        // Orphan blob (no metadata row) is cleaned on demand.
        var orphan = Path.Combine(session.VaultDirectory, "attachments", Guid.NewGuid() + ".pmblob");
        Directory.CreateDirectory(Path.GetDirectoryName(orphan)!);
        await File.WriteAllBytesAsync(orphan, [1, 2, 3]);
        Assert.Equal(1, attachments.CleanupOrphans(session));
        Assert.False(File.Exists(orphan));
    }

    [Fact]
    public async Task Attachment_Cannot_Cross_Vaults()
    {
        var lifecycle = Lifecycle();
        var (items, _, _, attachments) = Services();
        using var sessionA = await VaultAsync(lifecycle);
        using var sessionB = await VaultAsync(lifecycle);

        var itemA = await items.CreateAsync(sessionA, new ItemDraft { Kind = ItemType.SecureNote, Title = "A" });
        var source = Path.Combine(_root, "cross.txt");
        await File.WriteAllBytesAsync(source, [9, 9, 9]);
        var added = await attachments.AddAsync(sessionA, itemA.Id, source);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => attachments.ExportAsync(sessionB, added.Id, Path.Combine(_root, "stolen.bin")));
    }

    // ── Search ──

    [Fact]
    public async Task Search_Covers_All_Kinds_Folders_Tags_And_Excludes_Trash()
    {
        var (items, folders, tags, _) = Services();
        using var session = await VaultAsync(Lifecycle());

        var folder = await folders.CreateAsync(session, "پروژه سرور", null);
        var tag = await tags.CreateAsync(session, "مهم");
        var note = await items.CreateAsync(session, new ItemDraft
        {
            Kind = ItemType.SecureNote, Title = "راهنمای دیپلوی", FolderId = folder.Id, TagIds = [tag.Id],
        });
        var trashed = await items.CreateAsync(session, new ItemDraft { Kind = ItemType.SecureNote, Title = "راهنمای قدیمی" });
        await items.TrashAsync(session, trashed.Id);

        var live = await items.BuildSearchIndexAsync(session);
        static int Count(IReadOnlyList<CredentialSearchEntry> entries, string q) =>
            entries.Count(e => SearchNormalizer.Matches(e.SearchText, SearchNormalizer.Normalize(q)));

        Assert.Equal(1, Count(live, "دیپلوی"));
        Assert.Equal(1, Count(live, "سرور"));      // folder name is searchable
        Assert.Equal(1, Count(live, "مهم"));       // tag name is searchable
        Assert.Equal(0, Count(live, "قدیمی"));     // trashed items excluded from the live index

        var trashedIndex = await items.BuildSearchIndexAsync(session, new ItemFilter(TrashedOnly: true));
        Assert.Equal(1, Count(trashedIndex, "قدیمی"));
        _ = note;
    }

    // ── Migration ──

    [Fact]
    public async Task Phase3_Vault_Migrates_And_Logins_Remain_Readable()
    {
        // Create a Phase 3 vault with the legacy credential service, reopen with the current code
        // (runs migrations), verify the legacy payload is still readable through the unified path.
        var lifecycle = Lifecycle();
        var legacyCredentials = new CredentialService();
        using var session = await VaultAsync(lifecycle);
        var created = await legacyCredentials.CreateAsync(session, new CredentialDraft
        {
            Title = "phase3-ورود",
            Username = "legacy@x.ir",
            Password = "legacy-رمز",
            WebsiteUrl = "https://legacy.example",
        });
        var vaultIdText = session.VaultIdText;
        session.Lock();

        using var reopened = await lifecycle.UnlockWithPasswordAsync(
            session.VaultDirectory, "p4-رمز-1!".ToCharArray());

        Assert.Equal(vaultIdText, reopened.VaultIdText);
        Assert.Equal(2, new SchemaMigratorProbe(reopened).Version());
        Assert.Single(await legacyCredentials.ListAsync(reopened));
        var detail = await legacyCredentials.GetAsync(reopened, created.Id);
        Assert.Equal("phase3-ورود", detail.Title);
        Assert.Equal("legacy-رمز", detail.Password);

        // The unified item service reads the same legacy payload via the v0-compat deserializer.
        var unified = await items_service().GetAsync(reopened, created.Id);
        Assert.Equal("legacy-رمز", unified.Data["password"]);
        return;

        VaultItemService items_service() => new();
    }

    private sealed class SchemaMigratorProbe(VaultSession session)
    {
        public long Version() =>
            long.TryParse(session.Database.Connection
                .ScalarText("SELECT COALESCE(MAX(version), 0) FROM schema_migrations;"), out var parsed)
                ? parsed
                : 0;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
