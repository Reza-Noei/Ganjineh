using Ganjineh.Application.Abstractions;
using Ganjineh.Application.Credentials;
using Ganjineh.Application.Vaults;
using Ganjineh.Infrastructure.Settings;
using Ganjineh.Persistence;
using Ganjineh.Persistence.IO;
using Ganjineh.Persistence.Registry;
using Xunit;

namespace Ganjineh.IntegrationTests;

/// <summary>Phase 3 service-level coverage: catalog, credentials CRUD, search, session safety.</summary>
public sealed class Phase3ServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ganjineh-p3").FullName;

    private (VaultLifecycleService Lifecycle, VaultCatalog Catalog, CredentialService Credentials) Services() =>
        (new VaultLifecycleService(registryFactory: Registry), new VaultCatalog(Registry), new CredentialService());

    private VaultRegistry Registry() => new(Path.Combine(_root, "vaults.json"));

    private async Task<VaultSession> CreateVaultAsync(VaultLifecycleService lifecycle, string name)
    {
        var created = await lifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = "phase3-رمز-1!".ToCharArray() });
        _ = created.RecoveryKey; // caller keeps recovery management implicit in these tests
        await lifecycle.RenameVaultAsync(created.Session, name);
        return created.Session;
    }

    // ── Vault catalog ──

    [Fact]
    public async Task Catalog_Lists_Orders_And_Removes_Without_Deleting_Files()
    {
        var (lifecycle, catalog, _) = Services();
        using var session = await CreateVaultAsync(lifecycle, "کار");
        var path = session.VaultDirectory;

        var list = catalog.List();
        var entry = Assert.Single(list);
        Assert.Equal("کار", entry.LocalLabel);
        Assert.Equal(path, entry.Path);

        catalog.RemoveFromList(entry.VaultId);
        Assert.Empty(catalog.List());
        Assert.True(Directory.Exists(path)); // files untouched by remove-from-list
    }

    [Fact]
    public async Task DeleteVault_Removes_Files_And_Registry_Entry()
    {
        var (lifecycle, catalog, _) = Services();
        using var session = await CreateVaultAsync(lifecycle, "delete-me");
        var path = session.VaultDirectory;
        session.Lock();

        catalog.DeleteVault(new Guid(session.Header.VaultId).ToString());

        Assert.False(Directory.Exists(path));
        Assert.Empty(catalog.List());
    }

    [Fact]
    public async Task Rename_Writes_Encrypted_Name_And_Registry_Label()
    {
        var (lifecycle, catalog, _) = Services();
        using var session = await CreateVaultAsync(lifecycle, "اول");
        var vaultId = new Guid(session.Header.VaultId).ToString();

        await lifecycle.RenameVaultAsync(session, "گنجینه اصلی");

        Assert.Equal("گنجینه اصلی", lifecycle.GetVaultName(session));
        Assert.Equal("گنجینه اصلی", catalog.List().Single(v => v.VaultId == vaultId).LocalLabel);

        // Cryptographic identity untouched.
        Assert.Equal(vaultId, new Guid(session.Header.VaultId).ToString());
    }

    // ── Credential CRUD ──

    [Fact]
    public async Task Credential_RoundTrip_With_Unicode_Data()
    {
        var (lifecycle, _, credentials) = Services();
        using var session = await CreateVaultAsync(lifecycle, "roundtrip");

        var created = await credentials.CreateAsync(session, new CredentialDraft
        {
            Title = "بانک مرکزی — پورتال ادمین",
            Username = "admin@example.ir",
            Password = "رمز-سخت-🔒۱۲۳",
            WebsiteUrl = "https://portal.example.ir/login",
            Notes = "یادداشت با متن فارسی و English mixed.",
        });

        var detail = await credentials.GetAsync(session, created.Id);
        Assert.Equal("بانک مرکزی — پورتال ادمین", detail.Title);
        Assert.Equal("admin@example.ir", detail.Username);
        Assert.Equal("رمز-سخت-🔒۱۲۳", detail.Password);
        Assert.Equal("یادداشت با متن فارسی و English mixed.", detail.Notes);

        var updated = await credentials.UpdateAsync(session, created.Id, new CredentialDraft
        {
            Title = "بانک مرکزی",
            Username = "admin2@example.ir",
            Password = "رمز-جدید-456",
            WebsiteUrl = "https://portal.example.ir/v2",
        });
        Assert.Equal("بانک مرکزی", updated.Title);
        Assert.True(updated.UpdatedAt >= created.UpdatedAt);

        var reread = await credentials.GetAsync(session, created.Id);
        Assert.Equal("رمز-جدید-456", reread.Password);
        Assert.Null(reread.Notes); // omitted fields clear

        await credentials.DeleteAsync(session, created.Id);
        Assert.Empty(await credentials.ListAsync(session));
    }

    [Fact]
    public async Task Validation_Rejects_Empty_Title_And_Overlong_Fields()
    {
        var (lifecycle, _, credentials) = Services();
        using var session = await CreateVaultAsync(lifecycle, "validation");

        await Assert.ThrowsAsync<ValidationException>(() => credentials.CreateAsync(session,
            new CredentialDraft { Title = "  " }));

        await Assert.ThrowsAsync<ValidationException>(() => credentials.CreateAsync(session,
            new CredentialDraft { Title = new string('ع', 201) }));

        await Assert.ThrowsAsync<ValidationException>(() => credentials.CreateAsync(session,
            new CredentialDraft { Title = "ok", Password = new string('x', 1001) }));
    }

    [Fact]
    public async Task Operations_On_Locked_Or_Stale_Session_Are_Rejected()
    {
        var (lifecycle, _, credentials) = Services();
        var session = await CreateVaultAsync(lifecycle, "stale");
        var created = await credentials.CreateAsync(session, new CredentialDraft { Title = "t" });

        session.Lock();
        await Assert.ThrowsAsync<StaleSessionException>(
            () => credentials.ListAsync(session));
        await Assert.ThrowsAsync<StaleSessionException>(
            () => credentials.GetAsync(session, created.Id));
    }

    [Fact]
    public async Task Credentials_Never_Cross_Vaults()
    {
        var (lifecycle, _, credentials) = Services();
        using var vaultA = await CreateVaultAsync(lifecycle, "A");
        using var vaultB = await CreateVaultAsync(lifecycle, "B");

        var inA = await credentials.CreateAsync(vaultA, new CredentialDraft { Title = "عنوان A" });

        var listA = await credentials.ListAsync(vaultA);
        var listB = await credentials.ListAsync(vaultB);
        _ = inA;
        Assert.Single(listA);
        Assert.Empty(listB); // B never sees A's items

        // Reading A's item through B's session must fail (identity + AAD binding).
        await Assert.ThrowsAnyAsync<Exception>(() => credentials.GetAsync(vaultB, inA.Id));
    }

    // ── Search normalization ──

    [Fact]
    public void Search_Normalization_Folds_Persian_Variants()
    {
        Assert.Equal(SearchNormalizer.Normalize("ملی"), SearchNormalizer.Normalize("ملى"));       // ي/ی
        Assert.Equal(SearchNormalizer.Normalize("کار"), SearchNormalizer.Normalize("كار"));       // ك/ک
        Assert.Equal(SearchNormalizer.Normalize("الهام"), SearchNormalizer.Normalize("إلهام"));   // alef variants
        Assert.Equal(SearchNormalizer.Normalize("رمزحر"), SearchNormalizer.Normalize("رمز‌حر")); // ZWNJ
        Assert.Equal(SearchNormalizer.Normalize("سلام"), SearchNormalizer.Normalize("سَلامْ"));   // diacritics
    }

    [Fact]
    public async Task Search_Finds_By_Title_Username_And_Url_But_Not_Password()
    {
        var (lifecycle, _, credentials) = Services();
        using var session = await CreateVaultAsync(lifecycle, "search");

        await credentials.CreateAsync(session, new CredentialDraft
        {
            Title = "ایمیل کاری",
            Username = "kari@example.com",
            Password = "NotSearchable-رمز-777",
            WebsiteUrl = "https://mail.example.com",
            Notes = "یادداشت تستی",
        });

        var index = await credentials.BuildSearchIndexAsync(session);
        static int Count(IReadOnlyList<CredentialSearchEntry> entries, string query) =>
            entries.Count(e => SearchNormalizer.Matches(e.SearchText, SearchNormalizer.Normalize(query)));

        Assert.Equal(1, Count(index, "ایمیل"));          // title
        Assert.Equal(1, Count(index, "کاری"));           // username (Latin)
        Assert.Equal(1, Count(index, "mail.example"));   // URL
        Assert.Equal(1, Count(index, "یادداشت تستی"));   // notes are searchable

        // The password is never present in any search surface:
        Assert.Equal(0, Count(index, "NotSearchable"));
        Assert.Equal(0, Count(index, "رمز-777"));
        Assert.All(index, e => Assert.DoesNotContain("NotSearchable", e.SearchText, StringComparison.Ordinal));
    }

    private static string Normalize(string q) => SearchNormalizer.Normalize(q);

    // ── Encrypted persistence ──

    [Fact]
    public async Task Credential_Payload_Is_Encrypted_At_Rest()
    {
        var (lifecycle, _, credentials) = Services();
        using var session = await CreateVaultAsync(lifecycle, "atrest");
        var sentinel = "TOPSECRET-رمز-424242";

        await credentials.CreateAsync(session, new CredentialDraft
        {
            Title = "sentinel-item",
            Password = sentinel,
        });
        session.Lock();

        var dbBytes = await File.ReadAllBytesAsync(Path.Combine(session.VaultDirectory, "vault.db"));
        var walBytes = File.Exists(Path.Combine(session.VaultDirectory, "vault.db-wal"))
            ? await File.ReadAllBytesAsync(Path.Combine(session.VaultDirectory, "vault.db-wal"))
            : [];
        var needles = new[]
        {
            System.Text.Encoding.UTF8.GetBytes(sentinel),
            System.Text.Encoding.Unicode.GetBytes(sentinel),
            System.Text.Encoding.UTF8.GetBytes("sentinel-item"),
        };
        foreach (var needle in needles)
        {
            Assert.Equal(-1, dbBytes.AsSpan().IndexOf(needle));
            Assert.Equal(-1, walBytes.AsSpan().IndexOf(needle));
        }
    }

    private sealed class ByteCompare : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[]? x, byte[]? y)
        {
            if (x is null || y is null)
            {
                return ReferenceEquals(x, y);
            }

            return x.AsSpan().SequenceEqual(y);
        }

        public int GetHashCode(byte[] obj) => obj.Length;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
