using PasswordManager.Application.Vaults;
using PasswordManager.Persistence;
using PasswordManager.Persistence.IO;
using PasswordManager.Persistence.Registry;
using PasswordManager.Persistence.VaultHeader;
using Xunit;

namespace PasswordManager.IntegrationTests;

/// <summary>
/// Crash safety at security-update boundaries: an interrupted header rewrite must leave the
/// previous header byte-identical and fully usable, and stale temp files must be cleaned on open.
/// </summary>
public sealed class CrashSafetyTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ganjineh-crash").FullName;

    private VaultLifecycleService Service() =>
        new(registryFactory: () => new VaultRegistry(Path.Combine(_root, "vaults.json")));

    private static ReadOnlySpan<char> Password => "crash-رمز-1!".AsSpan();

    [Fact]
    public async Task Interrupted_Before_Replace_Keeps_Previous_Header_Fully_Working()
    {
        var service = Service();
        using var created = await service.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = Password.ToArray() });
        var directory = created.Session.VaultDirectory;
        var headerPath = Path.Combine(directory, "header.bin");
        var headerBefore = await File.ReadAllBytesAsync(headerPath);
        var recoveryBefore = created.RecoveryKeyEncoded;

        // Simulate a crash inside the atomic replace during a master-password change.
        var store = new HeaderFileStore();
        var updated = store.Read(headerPath) with
        {
            PasswordSalt = Enumerable.Repeat((byte)0xEE, 16).ToArray(),
        };
        Assert.Throws<IOException>(() => store.WriteUpdated(
            headerPath, updated, created.Session.Keyring.HeaderMacKey, AtomicFaultPoint.BeforeReplace));

        // Original header byte-identical; old password + old recovery still work.
        Assert.Equal(headerBefore, await File.ReadAllBytesAsync(headerPath));
        created.Session.Lock();
        using var session = await service.UnlockWithPasswordAsync(directory, Password.ToArray());
        Assert.Equal(VaultSessionState.Unlocked, session.State);
        session.Lock();
        using var viaRecovery = await service.UnlockWithRecoveryKeyAsync(directory, recoveryBefore);
        Assert.Equal(VaultSessionState.Unlocked, viaRecovery.State);
    }

    [Fact]
    public async Task Interrupted_After_Temp_Write_Leaves_Original_And_Temp_Is_Cleaned()
    {
        var service = Service();
        using var created = await service.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = Password.ToArray() });
        var directory = created.Session.VaultDirectory;
        var headerPath = Path.Combine(directory, "header.bin");
        var headerBefore = await File.ReadAllBytesAsync(headerPath);

        var store = new HeaderFileStore();
        var updated = store.Read(headerPath) with
        {
            PasswordSalt = Enumerable.Repeat((byte)0xDD, 16).ToArray(),
        };
        Assert.Throws<IOException>(() => store.WriteUpdated(
            headerPath, updated, created.Session.Keyring.HeaderMacKey, AtomicFaultPoint.AfterTempWrite));

        Assert.True(File.Exists(headerPath + ".tmp"), "test premise: temp file was written");
        Assert.Equal(headerBefore, await File.ReadAllBytesAsync(headerPath));

        // Next open cleans the stale temp file and the vault opens normally.
        created.Session.Lock();
        using var session = await service.UnlockWithPasswordAsync(directory, Password.ToArray());
        Assert.False(File.Exists(headerPath + ".tmp"), "stale temp file must be cleaned on open");
    }

    [Fact]
    public async Task Rollback_To_Older_Header_Snapshot_Triggers_Rollback_Warning()
    {
        var service = Service();
        using var created = await service.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = Password.ToArray() });
        var directory = created.Session.VaultDirectory;
        var headerPath = Path.Combine(directory, "header.bin");
        Assert.Equal(1UL, created.Session.Header.HeaderCounter);

        // Snapshot the counter-1 header, then change the master password (counter 2).
        var olderHeader = await File.ReadAllBytesAsync(headerPath);
        var oldPassword = Password.ToArray();
        var newPassword = "new-رمز-2!".ToCharArray();
        await service.ChangeMasterPasswordAsync(created.Session, oldPassword, newPassword);
        Assert.Equal(2UL, created.Session.Header.HeaderCounter);
        created.Session.Lock();

        // Opening with the new password against the current file: no warning (counter == high-water).
        using var current = await service.UnlockWithPasswordAsync(directory, newPassword);
        Assert.False(current.RollbackWarning);
        current.Lock();

        // Whole-file rollback to the older snapshot: the OLD password unlocks (old wrap is valid)
        // and the session must carry the rollback warning (counter regressed vs registry).
        File.WriteAllBytes(headerPath, olderHeader);
        using var rolledBack = await service.UnlockWithPasswordAsync(directory, oldPassword);
        Assert.True(rolledBack.RollbackWarning);
        Assert.Equal(1UL, rolledBack.Header.HeaderCounter);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
