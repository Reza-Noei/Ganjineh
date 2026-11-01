using System.Text;
using Ganjineh.Application.Backup;
using Ganjineh.Application.Credentials;
using Ganjineh.Application.Items;
using Ganjineh.Application.Security;
using Ganjineh.Application.Vaults;
using Ganjineh.Persistence.Items;
using Ganjineh.Persistence.Registry;
using Xunit;

namespace Ganjineh.IntegrationTests;

/// <summary>Phase 6: backup/verify/restore/import/export end-to-end on real files.</summary>
public sealed class Phase6Tests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ganjineh-p6").FullName;

    private VaultLifecycleService Lifecycle() =>
        new(registryFactory: () => new VaultRegistry(Path.Combine(_root, "vaults.json")));

    private (VaultBackupService Backup, VaultItemService Items, AttachmentService Attachments) Services() =>
        (new VaultBackupService(), new VaultItemService(), new AttachmentService());

    [Fact]
    public async Task Backup_Verify_Rehearsal_And_Restore_RoundTrip()
    {
        var lifecycle = Lifecycle();
        var (backup, items, attachments) = Services();
        var created = await lifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = "p6-رمز-1!".ToCharArray() });
        var session = created.Session;

        await items.CreateAsync(session, new ItemDraft
        {
            Kind = ItemType.Login, Title = "بانک — ورود", Data = new Dictionary<string, object?>
            {
                ["username"] = "ali@x.ir", ["password"] = "رمز-سخت-99", ["urls"] = new List<string> { "https://b.ir" },
            },
        });

        var noteFile = Path.Combine(_root, "note.txt");
        await File.WriteAllBytesAsync(noteFile, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        var noteItem = await items.CreateAsync(session, new ItemDraft { Kind = ItemType.SecureNote, Title = "with-att" });
        await attachments.AddAsync(session, noteItem.Id, noteFile);

        var backupPath = Path.Combine(_root, "full.gvbak");
        var result = await backup.CreateAsync(session, new BackupRequest { DestinationFilePath = backupPath });
        Assert.True(result.Success, result.ErrorKey);

        // Cryptographic verification.
        var crypto = await backup.VerifyAsync(backupPath, session, VerificationLevel.Cryptographic);
        Assert.True(crypto.Success, "DIAG:" + crypto.ErrorKey);
        Assert.Equal(2, crypto.ItemCount);
        Assert.Equal(1, crypto.AttachmentCount);

        // Rehearsal restores into a temp location and opens the real database.
        var rehearsal = await backup.VerifyAsync(backupPath, session, VerificationLevel.Rehearsal);
        Assert.True(rehearsal.Success, rehearsal.ErrorKey);
        Assert.Equal(2, rehearsal.ItemCount);
    }

    [Fact]
    public async Task Full_Restore_On_Fresh_Registry_Portably_Unlocks()
    {
        var lifecycle = Lifecycle();
        var (backup, items, _) = Services();
        var created = await lifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = "portable-رمز-2!".ToCharArray() });
        var originalId = created.Session.Header.VaultId;
        await items.CreateAsync(created.Session, new ItemDraft
        {
            Kind = ItemType.Login, Title = "porter", Data = new Dictionary<string, object?> { ["password"] = "inner-رمز" },
        });
        var backupPath = Path.Combine(_root, "portable.gvbak");
        Assert.True((await backup.CreateAsync(created.Session,
            new BackupRequest { DestinationFilePath = backupPath })).Success);
        created.Session.Lock();

        // "Another machine": empty temp registry, fresh destination.
        var newMachine = Path.Combine(_root, "newmachine");
        Directory.CreateDirectory(newMachine);
        var freshLifecycle = new VaultLifecycleService(
            registryFactory: () => new VaultRegistry(Path.Combine(newMachine, "vaults.json")));
        var backup2 = new VaultBackupService();

        var restore = await backup2.RestoreAsync(new RestoreRequest
        {
            BackupFilePath = backupPath,
            DestinationParentDirectory = newMachine,
            CredentialPassword = "portable-رمز-2!",
        });

        Assert.True(restore.Success, restore.ErrorKey);
        Assert.Equal(new Guid(originalId).ToString(), restore.VaultId?.ToString());
        Assert.NotNull(restore.Session);
        Assert.True(Directory.Exists(restore.VaultDirectory));

        // The restored vault opens with the same master password through the lifecycle too.
        using var reopened = await freshLifecycle.UnlockWithPasswordAsync(
            restore.VaultDirectory!, "portable-رمز-2!".ToCharArray());
        var logins = await items.ListAsync(reopened);
        Assert.Single(logins);
        Assert.Equal("porter", logins[0].Title);
    }

    [Fact]
    public async Task Wrong_Credential_Is_Rejected()
    {
        var lifecycle = Lifecycle();
        var (backup, items, _) = Services();
        var created = await lifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = "right-رمز-3!".ToCharArray() });
        await items.CreateAsync(created.Session, new ItemDraft { Kind = ItemType.Login, Title = "x" });
        var backupPath = Path.Combine(_root, "w.gvbak");
        await backup.CreateAsync(created.Session, new BackupRequest { DestinationFilePath = backupPath });
        created.Session.Lock();

        var restore = await backup.RestoreAsync(new RestoreRequest
        {
            BackupFilePath = backupPath,
            DestinationParentDirectory = Path.Combine(_root, "restore-dest"),
            CredentialPassword = "wrong-رمز!",
        });

        Assert.False(restore.Success);
        Assert.Equal("Unlock.WrongPassword", restore.ErrorKey);
    }

    [Fact]
    public async Task Recovery_Key_Unlocks_Backup_And_Resets_Master_Password()
    {
        var lifecycle = Lifecycle();
        var (backup, items, _) = Services();
        var created = await lifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = "will-forget-رمز!".ToCharArray() });
        var recoveryKey = created.RecoveryKeyEncoded;
        var backupPath = Path.Combine(_root, "recovery.gvbak");
        await backup.CreateAsync(created.Session, new BackupRequest { DestinationFilePath = backupPath });
        created.Session.Lock();

        // Restore using the Recovery Key instead of the (forgotten) master password.
        var restore = await backup.RestoreAsync(new RestoreRequest
        {
            BackupFilePath = backupPath,
            DestinationParentDirectory = Path.Combine(_root, "restore-dest"),
            CredentialPassword = recoveryKey,
        });

        Assert.True(restore.Success, restore.ErrorKey);
        Assert.NotNull(restore.Session);

        // Reset the master password on the restored vault (Phase 2 mechanism, reused).
        var lifecycle2 = new VaultLifecycleService(
            registryFactory: () => new VaultRegistry(Path.Combine(_root, "vaults.json")));
        await lifecycle2.ResetMasterPasswordWithRecoveryKeyAsync(
            restore.VaultDirectory!, recoveryKey, "new-رمز-after-reset!".ToCharArray());

        restore.Session.Lock();
        using var reopened = await lifecycle2.UnlockWithPasswordAsync(
            restore.VaultDirectory!, "new-رمز-after-reset!".ToCharArray());
        Assert.True(reopened.State == VaultSessionState.Unlocked);
    }

    [Fact]
    public async Task Tampered_Backup_Is_Detected()
    {
        var lifecycle = Lifecycle();
        var (backup, items, _) = Services();
        var created = await lifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = "tamper-رمز-4!".ToCharArray() });
        await items.CreateAsync(created.Session, new ItemDraft { Kind = ItemType.Login, Title = "t" });
        var backupPath = Path.Combine(_root, "t.gvbak");
        Assert.True((await backup.CreateAsync(created.Session,
            new BackupRequest { DestinationFilePath = backupPath })).Success);
        created.Session.Lock();

        // Tamper deep inside the encrypted package region.
        var bytes = await File.ReadAllBytesAsync(backupPath);
        bytes[^40] ^= 0xFF;
        await File.WriteAllBytesAsync(backupPath, bytes);

        var verify = await backup.VerifyAsync(backupPath, created.Session, VerificationLevel.Cryptographic);
        Assert.False(verify.Success);
    }

    [Fact]
    public async Task Truncated_Backup_Fails_Structural_Verification()
    {
        var lifecycle = Lifecycle();
        var (backup, items, _) = Services();
        var created = await lifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = "trunc-رمز-5!".ToCharArray() });
        await items.CreateAsync(created.Session, new ItemDraft { Kind = ItemType.Login, Title = "t" });
        var backupPath = Path.Combine(_root, "tr.gvbak");
        await backup.CreateAsync(created.Session, new BackupRequest { DestinationFilePath = backupPath });
        created.Session.Lock();

        var bytes = await File.ReadAllBytesAsync(backupPath);
        await File.WriteAllBytesAsync(backupPath, bytes[..(bytes.Length - 64)]);

        var verify = await backup.VerifyAsync(backupPath, created.Session, VerificationLevel.Structural);
        Assert.False(verify.Success);
    }

    // ── Selective restore ──

    [Fact]
    public async Task Selective_Restore_ReEncrypts_Into_Another_Vault()
    {
        var lifecycle = Lifecycle();
        var (backup, items, attachments) = Services();
        var source = await lifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = "sel-src-رمز!".ToCharArray() });

        var sourceFile = Path.Combine(_root, "att-source.bin");
        await File.WriteAllBytesAsync(sourceFile, new byte[] { 7, 7, 7, 7 });
        var sourceItem = await items.CreateAsync(source.Session, new ItemDraft
        {
            Kind = ItemType.Login,
            Title = "selective-item",
            Data = new Dictionary<string, object?> { ["username"] = "sel@x.ir", ["password"] = "sel-رمز" },
        });
        await attachments.AddAsync(source.Session, sourceItem.Id, sourceFile);
        var backupPath = Path.Combine(_root, "sel.gvbak");
        Assert.True((await backup.CreateAsync(source.Session,
            new BackupRequest { DestinationFilePath = backupPath })).Success);

        // Destination: an independent vault with its own keys.
        var targetLifecycle = new VaultLifecycleService(
            registryFactory: () => new VaultRegistry(Path.Combine(_root, "vaults2.json")));
        using var target = await targetLifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = "sel-dst-رمز!".ToCharArray() });

        var service = new VaultBackupService();
        var result = await service.RestoreSelectiveAsync(new SelectiveRestoreRequest
        {
            BackupFilePath = backupPath,
            CredentialPassword = "sel-src-رمز!",
            TargetSession = target.Session,
            ItemIds = [sourceItem.Id],
            Policy = SelectiveConflictPolicy.CreateCopy,
        });

        Assert.Equal(1, result.Created);

        // The copied item decrypts correctly under the TARGET vault keys.
        var copiedList = await items.ListAsync(target.Session);
        Assert.Single(copiedList);
        var copied = await items.GetAsync(target.Session, copiedList[0].Id);
        Assert.Equal("selective-item", copied.Title);
        Assert.Equal("sel@x.ir", copied.Data["username"]?.ToString());
        Assert.Equal("sel-رمز", copied.Data["password"]?.ToString());

        // The attachment was re-encrypted under target keys and exports correctly.
        var restoredAttachments = await attachments.ListAsync(target.Session, copiedList[0].Id);
        var attachment = Assert.Single(restoredAttachments);
        var exportPath = Path.Combine(_root, "att-export.bin");
        await attachments.ExportAsync(target.Session, attachment.Id, exportPath);
        Assert.Equal(await File.ReadAllBytesAsync(sourceFile), await File.ReadAllBytesAsync(exportPath));
    }

    [Fact]
    public async Task Retention_Prune_Keeps_Newest()
    {
        var lifecycle = Lifecycle();
        var (backup, items, _) = Services();
        using var session = await lifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = "prune-رمز-6!".ToCharArray() })
            .ContinueWith(t => t.Result.Session);
        var backupDir = Path.Combine(_root, "backups");
        Directory.CreateDirectory(backupDir);

        for (var i = 0; i < 3; i++)
        {
            var path = Path.Combine(backupDir, $"b{i}.gvbak");
            var result = await backup.CreateAsync(session,
                new BackupRequest { DestinationFilePath = path });
            Assert.True(result.Success, result.ErrorKey);
            await Task.Delay(30);
        }

        var removed = backup.PruneBackups(backupDir, keepCount: 2);
        Assert.Equal(1, removed);
        Assert.Equal(2, Directory.EnumerateFiles(backupDir, "*.gvbak").Count());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
