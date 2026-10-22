using System.Text;
using PasswordManager.Application.Backup;
using PasswordManager.Application.Items;
using PasswordManager.Application.Security;
using PasswordManager.Application.Vaults;
using PasswordManager.Cryptography.Aead;
using PasswordManager.Cryptography.Errors;
using PasswordManager.Cryptography.Generation;
using PasswordManager.Cryptography.Kdf;
using PasswordManager.Cryptography.Otp;
using PasswordManager.Cryptography.Random;
using PasswordManager.Cryptography.Secrets;
using PasswordManager.Persistence;
using PasswordManager.Persistence.Items;
using PasswordManager.Persistence.Registry;
using PasswordManager.Persistence.VaultHeader;
using Xunit;
using Xunit.Abstractions;

namespace PasswordManager.IntegrationTests;

/// <summary>
/// Phase 8 adversarial tests: probe the actual attack surfaces documented in the threat model.
/// Each test either confirms a security property or documents a finding.
/// </summary>
public sealed class AdversarialSecurityTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ganjineh-p8adv").FullName;
    private readonly ITestOutputHelper _output;

    public AdversarialSecurityTests(ITestOutputHelper output) => _output = output;

    private VaultLifecycleService Lifecycle() =>
        new(registryFactory: () => new VaultRegistry(Path.Combine(_root, "vaults.json")));

    private (VaultBackupService Backup, VaultItemService Items, AttachmentService Attachments) Services() =>
        (new VaultBackupService(), new VaultItemService(), new AttachmentService());

    private const string MasterPassword = "adv-رمز-Phase8!";

    // ── 1. Header fuzzing: malformed inputs must not crash or corrupt state ──

    [Fact]
    public async Task Fuzz_Header_Bit_Flips_Produce_Typed_Errors()
    {
        var lifecycle = Lifecycle();
        using var created = await lifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = MasterPassword.ToCharArray() });
        var headerPath = Path.Combine(created.Session.VaultDirectory, "header.bin");
        created.Session.Lock();
        var original = await File.ReadAllBytesAsync(headerPath);

        var rnd = new Random(20261022);
        for (var round = 0; round < 2000; round++)
        {
            var mutated = (byte[])original.Clone();
            mutated[rnd.Next(mutated.Length)] ^= (byte)(1 << rnd.Next(8));
            try
            {
                HeaderCodec.Parse(mutated);
            }
            catch (Exception ex) when (ex is CryptoFormatException or UnsupportedFormatException or CryptoAuthenticationException)
            {
                // expected
            }
            catch (Exception ex)
            {
                Assert.Fail($"Non-typed exception at round {round}: {ex.GetType().Name} {ex.Message}");
            }
        }
        _output.WriteLine("Fuzz: 2000 mutations all produced typed errors or passed");
    }

    // ── 2. Backup entry names prevent path traversal ──

    [Fact]
    public async Task Backup_Entry_Names_Prevent_Path_Traversal()
    {
        var lifecycle = Lifecycle();
        var backup = new VaultBackupService();
        using var session = await lifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = MasterPassword.ToCharArray() })
            .ContinueWith(t => t.Result.Session);

        var package = new BackupFormat.PackageBuilder();
        package.Add("vault.db", Path.Combine(session.VaultDirectory, "vault.db"));
        var entries = package.EntryHeaders().ToList();

        Assert.All(entries, e => Assert.DoesNotContain("..", e.Name));
        Assert.All(entries, e => Assert.DoesNotContain('\\', e.Name));
    }

    // ── 3. Resource exhaustion via malicious KDF parameters ──

    [Fact]
    public void Malicious_Kdf_Parameters_Are_Rejected()
    {
        Assert.Throws<InvalidKdfParametersException>(() =>
            new KdfParameters(long.MaxValue, 3, 1).Validate());
        Assert.Throws<InvalidKdfParametersException>(() =>
            new KdfParameters(0x7FFFFFFF, 999999, 999).Validate());
    }

    // ── 4. Nonce uniqueness under same key (statistical) ──

    [Fact]
    public void Nonce_Uniqueness_High_Volume()
    {
        var aead = new AesGcmAuthenticatedEncryption();
        var key = new byte[32];
        var seen = new HashSet<string>();
        const int count = 10_000;
        for (var i = 0; i < count; i++)
        {
            var payload = aead.Encrypt("nonce-test"u8, key, KeyDomain.Item, "aad"u8);
            Assert.True(seen.Add(Convert.ToHexString(payload.Nonce)), $"nonce collision at {i}");
        }
        Assert.Equal(count, seen.Count);
    }

    // ── 5. Plaintext leakage scan across all output files ──

    [Fact]
    public async Task No_Plaintext_Secrets_In_Any_Output_File()
    {
        var lifecycle = Lifecycle();
        var (backup, items, _) = Services();
        var sentinelPassword = "TOPSECRET-sentinel-pwd-9999";
        var sentinelTitle = "SENTINEL-ITEM-TITLE-9999";

        using var session = await lifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = sentinelPassword.ToCharArray() })
            .ContinueWith(t => t.Result.Session);

        await items.CreateAsync(session, new ItemDraft
        {
            Kind = ItemType.Login, Title = sentinelTitle,
            Data = new Dictionary<string, object?>
            {
                ["username"] = "sentinel@x.ir",
                ["password"] = sentinelPassword,
            },
        });

        var backupPath = Path.Combine(_root, "sentinel.gvbak");
        Assert.True((await backup.CreateAsync(session,
            new BackupRequest { DestinationFilePath = backupPath })).Success);

        session.Lock();

        var filesToScan = new List<string>();
        foreach (var dir in Directory.GetDirectories(_root))
            filesToScan.AddRange(Directory.GetFiles(dir, "*", SearchOption.AllDirectories));
        filesToScan.AddRange(Directory.GetFiles(_root));

        foreach (var file in filesToScan)
        {
            if (File.ReadAllBytes(file) is not { Length: > 0 } fileBytes) continue;
            foreach (var needle in new[]
                     {
                         Encoding.UTF8.GetBytes(sentinelPassword),
                         Encoding.UTF8.GetBytes(sentinelTitle),
                         Encoding.Unicode.GetBytes(sentinelPassword),
                     })
            {
                Assert.Equal(-1, fileBytes.AsSpan().IndexOf(needle));
            }
        }
    }

    // ── 6. Selective restore: cross-vault re-encryption ──

    [Fact]
    public async Task Selective_Restore_Decrypts_Under_Target_Keys()
    {
        var lifecycle = Lifecycle();
        var items = new VaultItemService();
        var backup = new VaultBackupService();

        using var source = await lifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = "src-رمز-8!".ToCharArray() })
            .ContinueWith(t => t.Result.Session);
        var createdItem = await items.CreateAsync(source, new ItemDraft
        {
            Kind = ItemType.Login, Title = "source-item",
            Data = new Dictionary<string, object?> { ["username"] = "s@x.ir", ["password"] = "src-pwd" },
        });
        var backupPath = Path.Combine(_root, "sel.gvbak");
        Assert.True((await backup.CreateAsync(source, new BackupRequest { DestinationFilePath = backupPath })).Success);
        source.Lock();

        var targetLifecycle = new VaultLifecycleService(
            registryFactory: () => new VaultRegistry(Path.Combine(_root, "target-vaults.json")));
        using var target = await targetLifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = "tgt-رمز-9!".ToCharArray() })
            .ContinueWith(t => t.Result.Session);

        var service = new VaultBackupService();
        var result = await service.RestoreSelectiveAsync(new SelectiveRestoreRequest
        {
            BackupFilePath = backupPath,
            CredentialPassword = "src-رمز-8!",
            TargetSession = target,
            ItemIds = [createdItem.Id],
            Policy = SelectiveConflictPolicy.CreateCopy,
        });

        Assert.Equal(1, result.Created);

        var targetItems = await items.ListAsync(target);
        var restoredItem = Assert.Single(targetItems);
        Assert.Equal("source-item", restoredItem.Title);

        var restoredDetail = await items.GetAsync(target, restoredItem.Id);
        Assert.Equal("src-pwd", restoredDetail.Data["password"]?.ToString());
    }

    // ── 7. Attachment tag tamper detection ──

    [Fact]
    public async Task Attachment_Tag_Tamper_Detected()
    {
        var lifecycle = Lifecycle();
        var items = new VaultItemService();
        var attachments = new AttachmentService();
        using var session = await lifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = MasterPassword.ToCharArray() })
            .ContinueWith(t => t.Result.Session);

        var item = await items.CreateAsync(session,
            new ItemDraft { Kind = ItemType.Login, Title = "tamper-att" });
        var sourceFile = Path.Combine(_root, "tamper-test.bin");
        await File.WriteAllBytesAsync(sourceFile, new byte[1000]);
        var att = await attachments.AddAsync(session, item.Id, sourceFile);

        var attDir = Path.Combine(session.VaultDirectory, "attachments");
        var blobFile = Directory.GetFiles(attDir, "*.pmblob").First();
        var blobBytes = await File.ReadAllBytesAsync(blobFile);
        blobBytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(blobFile, blobBytes);

        var exportPath = Path.Combine(_root, "tampered-export.bin");
        await Assert.ThrowsAnyAsync<Exception>(
            () => attachments.ExportAsync(session, att.Id, exportPath));
    }

    // ── 8. Locked session prevents item operations ──

    [Fact]
    public async Task Locked_Session_Prevents_Item_Operations()
    {
        var lifecycle = Lifecycle();
        var items = new VaultItemService();
        using var session = await lifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = MasterPassword.ToCharArray() })
            .ContinueWith(t => t.Result.Session);

        var created = await items.CreateAsync(session, new ItemDraft { Kind = ItemType.Login, Title = "lock-test" });
        session.Lock();

        await Assert.ThrowsAnyAsync<Exception>(() => items.ListAsync(session));
        await Assert.ThrowsAnyAsync<Exception>(() => items.GetAsync(session, created.Id));
        await Assert.ThrowsAnyAsync<Exception>(() => items.CreateAsync(session,
            new ItemDraft { Kind = ItemType.Login, Title = "post-lock" }));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
