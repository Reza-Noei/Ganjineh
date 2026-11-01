using System.Security.Cryptography;
using Ganjineh.Application.Vaults;
using Ganjineh.Cryptography.Errors;
using Ganjineh.Persistence.Registry;
using Xunit;

namespace Ganjineh.SecurityTests;

/// <summary>Secret hygiene: failure paths must never carry sensitive material in messages (ST-SEC-9).</summary>
public sealed class SecretHygieneTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ganjineh-hygiene").FullName;

    private VaultLifecycleService Service() =>
        new(registryFactory: () => new VaultRegistry(Path.Combine(_root, "vaults.json")));

    private const string Password = "hygiene-رمز-TopSecret!";

    [Fact]
    public async Task Authentication_Failures_Carry_No_Secrets()
    {
        var service = Service();
        using var created = await service.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = Password.ToCharArray() });
        var directory = created.Session.VaultDirectory;
        var recovery = created.RecoveryKeyEncoded;
        created.Session.Lock();

        try
        {
            var wrong = await Record.ExceptionAsync(
                () => service.UnlockWithPasswordAsync(directory, "totally-different-رمز".ToCharArray()));
            Assert.IsType<CryptoAuthenticationException>(wrong);
            Assert.Equal(string.Empty, wrong!.Message);

            var badKey = await Record.ExceptionAsync(
                () => service.UnlockWithRecoveryKeyAsync(directory, ValidFormatWrongRecovery()));
            Assert.IsType<CryptoAuthenticationException>(badKey);
            Assert.Equal(string.Empty, badKey!.Message);

            Assert.DoesNotContain(Password, wrong.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(recovery, wrong.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            var recoveryChars = recovery.ToCharArray();
            Array.Clear(recoveryChars);
        }
    }

    [Fact]
    public async Task Format_Errors_Do_Not_Quote_Input_Bytes()
    {
        var service = Service();
        using var created = await service.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = Password.ToCharArray() });
        var directory = created.Session.VaultDirectory;
        created.Session.Lock();

        var headerPath = Path.Combine(directory, "header.bin");
        var original = File.ReadAllBytes(headerPath);
        var tampered = (byte[])original.Clone();
        tampered[0x00A] ^= 0xFF; // unsupported format version field — inside an otherwise sensitive header
        File.WriteAllBytes(headerPath, tampered);

        var failure = await Record.ExceptionAsync(
            () => service.UnlockWithPasswordAsync(directory, Password.ToCharArray()));

        Assert.IsType<UnsupportedFormatException>(failure);
        var text = failure!.ToString();
        Assert.DoesNotContain("PMRK1", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
    }

    private static string ValidFormatWrongRecovery()
    {
        var secret = Enumerable.Range(5, 32).Select(i => (byte)i).ToArray();
        return Ganjineh.Cryptography.Recovery.RecoveryKeyCodec.Encode(secret);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
