using System.Text;
using Ganjineh.Application.Vaults;
using Ganjineh.Cryptography.Errors;
using Ganjineh.Persistence.Registry;
using Xunit;

namespace Ganjineh.IntegrationTests;

/// <summary>
/// Full lifecycle on real files: create, lock, both unlock paths, master-password change,
/// recovery rotation and reset, cross-vault isolation. Each test uses its own temp directory
/// and registry so no state leaks between tests or onto the real profile.
/// </summary>
public sealed class VaultLifecycleTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ganjineh-vaults").FullName;

    private VaultLifecycleService Service() =>
        new(registryFactory: () => new VaultRegistry(Path.Combine(_root, "vaults.json")));

    private string Password(string s = "درست-رمز-Test1!") => s;

    [Fact]
    public async Task Create_Produces_Unlocked_Session_And_Displayable_Recovery_Key()
    {
        var service = Service();
        using var created = await service.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = Password().ToCharArray() });

        Assert.Equal(VaultSessionState.Unlocked, created.Session.State);
        Assert.False(created.Session.RollbackWarning);
        Assert.StartsWith("PMRK1-", created.RecoveryKeyEncoded);
        Assert.True(Directory.Exists(created.Session.VaultDirectory));
        Assert.True(File.Exists(Path.Combine(created.Session.VaultDirectory, "header.bin")));
        Assert.True(File.Exists(Path.Combine(created.Session.VaultDirectory, "vault.db")));
    }

    [Fact]
    public async Task Lock_Then_Unlock_With_Password_RoundTrips()
    {
        var service = Service();
        using var created = await service.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = Password().ToCharArray() });
        var directory = created.Session.VaultDirectory;
        created.Session.Lock();
        Assert.Equal(VaultSessionState.Locked, created.Session.State);

        using var session = await service.UnlockWithPasswordAsync(directory, Password().ToCharArray());
        Assert.Equal(VaultSessionState.Unlocked, session.State);
    }

    [Fact]
    public async Task Wrong_Master_Password_Fails_With_Generic_Authentication_Error()
    {
        var service = Service();
        using var created = await service.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = Password().ToCharArray() });
        var directory = created.Session.VaultDirectory;
        created.Session.Lock();

        var wrong = Password("درست-رمز-WRONG");
        var failure = await Assert.ThrowsAsync<CryptoAuthenticationException>(
            () => service.UnlockWithPasswordAsync(directory, wrong.ToCharArray()));

        // No sensitive detail in the message.
        Assert.Equal(string.Empty, failure.Message);
    }

    [Fact]
    public async Task Unicode_Master_Password_RoundTrips()
    {
        var service = Service();
        var unicode = "رمز!🔒‌حرف";
        using var created = await service.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = unicode.ToCharArray() });
        var directory = created.Session.VaultDirectory;
        created.Session.Lock();

        using var session = await service.UnlockWithPasswordAsync(directory, unicode.ToCharArray());
        Assert.Equal(VaultSessionState.Unlocked, session.State);
    }

    [Fact]
    public async Task Recovery_Key_Unlocks_Without_Master_Password()
    {
        var service = Service();
        using var created = await service.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = Password().ToCharArray() });
        var directory = created.Session.VaultDirectory;
        created.Session.Lock();

        using var session = await service.UnlockWithRecoveryKeyAsync(directory, created.RecoveryKeyEncoded);
        Assert.Equal(VaultSessionState.Unlocked, session.State);
    }

    [Fact]
    public async Task Invalid_Recovery_Key_Is_Rejected()
    {
        var service = Service();
        using var created = await service.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = Password().ToCharArray() });
        var directory = created.Session.VaultDirectory;
        created.Session.Lock();

        await Assert.ThrowsAsync<CryptoFormatException>(
            () => service.UnlockWithRecoveryKeyAsync(directory, "PMRK1-AAAA-BBBB"));
        await Assert.ThrowsAsync<CryptoAuthenticationException>(
            () => service.UnlockWithRecoveryKeyAsync(directory, ValidFormatWrongKey()));
    }

    private static string ValidFormatWrongKey()
    {
        // A structurally valid PMRK1 string with a different random secret (checksum correct).
        var secret = Enumerable.Range(3, 32).Select(i => (byte)i).ToArray();
        return Ganjineh.Cryptography.Recovery.RecoveryKeyCodec.Encode(secret);
    }

    [Fact]
    public async Task Master_Password_Change_Old_Fails_New_Works_Recovery_Intact()
    {
        var service = Service();
        using var created = await service.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = Password().ToCharArray() });
        var directory = created.Session.VaultDirectory;
        var oldRecovery = created.RecoveryKeyEncoded;

        var newPassword = "new-رمز-42!";
        await service.ChangeMasterPasswordAsync(
            created.Session, Password().ToCharArray(), newPassword.ToCharArray());

        // Old password fails on the live vault.
        created.Session.Lock();
        await Assert.ThrowsAsync<CryptoAuthenticationException>(
            () => service.UnlockWithPasswordAsync(directory, Password().ToCharArray()));

        // New password works; recovery key from before the change still works.
        using var session = await service.UnlockWithPasswordAsync(directory, newPassword.ToCharArray());
        Assert.Equal(VaultSessionState.Unlocked, session.State);
        session.Lock();

        using var viaRecovery = await service.UnlockWithRecoveryKeyAsync(directory, oldRecovery);
        Assert.Equal(VaultSessionState.Unlocked, viaRecovery.State);
    }

    [Fact]
    public async Task Recovery_Rotation_Revokes_Old_Key_Keeps_Data_And_Password()
    {
        var service = Service();
        using var created = await service.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = Password().ToCharArray() });
        var directory = created.Session.VaultDirectory;
        var oldRecovery = created.RecoveryKeyEncoded;

        using var rotated = await service.RotateRecoveryKeyAsync(created.Session);

        Assert.StartsWith("PMRK1-", rotated.RecoveryKeyEncoded);
        Assert.NotEqual(oldRecovery, rotated.RecoveryKeyEncoded);

        // Data intact: an item-shaped write would be Phase 3; assert DB still opens under MP.
        created.Session.Lock();
        using var session = await service.UnlockWithPasswordAsync(directory, Password().ToCharArray());
        Assert.Equal(VaultSessionState.Unlocked, session.State);

        // Old key revoked on the live vault; new key works.
        session.Lock();
        await Assert.ThrowsAsync<CryptoAuthenticationException>(
            () => service.UnlockWithRecoveryKeyAsync(directory, oldRecovery));
        using var viaNew = await service.UnlockWithRecoveryKeyAsync(directory, rotated.RecoveryKeyEncoded);
        Assert.Equal(VaultSessionState.Unlocked, viaNew.State);
    }

    [Fact]
    public async Task Reset_Forgotten_Master_Password_With_Recovery()
    {
        var service = Service();
        using var created = await service.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = Password().ToCharArray() });
        var directory = created.Session.VaultDirectory;
        var recovery = created.RecoveryKeyEncoded;
        created.Session.Lock();

        var newPassword = "reset-رمز-77";
        using var session = await service.ResetMasterPasswordWithRecoveryKeyAsync(
            directory, recovery, newPassword.ToCharArray());

        Assert.Equal(VaultSessionState.Unlocked, session.State);

        // The old password no longer works; the recovery key still does.
        session.Lock();
        await Assert.ThrowsAsync<CryptoAuthenticationException>(
            () => service.UnlockWithPasswordAsync(directory, Password().ToCharArray()));
        using var again = await service.UnlockWithRecoveryKeyAsync(directory, recovery);
        Assert.Equal(VaultSessionState.Unlocked, again.State);
    }

    [Fact]
    public async Task Cross_Vault_Isolation_Same_Password_Different_Vaults()
    {
        var service = Service();
        using var vaultA = await service.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = Password().ToCharArray() });
        using var vaultB = await service.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = Password().ToCharArray() });

        Assert.NotEqual(vaultA.Session.Header.VaultId, vaultB.Session.Header.VaultId);
        Assert.NotEqual(vaultA.RecoveryKeyEncoded, vaultB.RecoveryKeyEncoded);

        // Close both sessions so the database files are readable.
        vaultA.Session.Lock();
        vaultB.Session.Lock();

        // Database keys differ (HKDF salt = VaultID): the raw DB files must differ.
        var dbA = await File.ReadAllBytesAsync(Path.Combine(vaultA.Session.VaultDirectory, "vault.db"));
        var dbB = await File.ReadAllBytesAsync(Path.Combine(vaultB.Session.VaultDirectory, "vault.db"));
        Assert.NotEqual(dbA, dbB);

        // A's recovery key must not open B (B is already locked).
        await Assert.ThrowsAsync<CryptoAuthenticationException>(
            () => service.UnlockWithRecoveryKeyAsync(vaultB.Session.VaultDirectory, vaultA.RecoveryKeyEncoded));
    }

    [Fact]
    public async Task Empty_Master_Password_Is_Refused()
    {
        var service = Service();
        await Assert.ThrowsAsync<PolicyViolationException>(
            () => service.CreateAsync(new CreateVaultRequest { Directory = _root, MasterPassword = Array.Empty<char>() }));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
