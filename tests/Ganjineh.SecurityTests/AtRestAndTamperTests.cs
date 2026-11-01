using System.Security.Cryptography;
using System.Text;
using PasswordManager.Cryptography;
using PasswordManager.Application.Vaults;
using PasswordManager.Cryptography.Errors;
using PasswordManager.Persistence;
using PasswordManager.Persistence.Registry;
using PasswordManager.Persistence.VaultHeader;
using Xunit;
using Xunit.Abstractions;

namespace PasswordManager.SecurityTests;

/// <summary>
/// Data-at-rest and adversarial-input guarantees (security-test-plan.md ST-DB-*, ST-SEC-3, ST-FZ-1).
/// </summary>
public sealed class AtRestAndTamperTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _root = Directory.CreateTempSubdirectory("ganjineh-sec").FullName;

    public AtRestAndTamperTests(ITestOutputHelper output) => _output = output;

    private VaultLifecycleService Service() =>
        new(registryFactory: () => new VaultRegistry(Path.Combine(_root, "vaults.json")));

    private const string MasterPassword = "at-rest-رمز-S3cret!";

    private async Task<(string Directory, string RecoveryKey, VaultSession Session)> CreateVaultAsync()
    {
        var service = Service();
        var created = await service.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = MasterPassword.ToCharArray() });
        return (created.Session.VaultDirectory, created.RecoveryKeyEncoded, created.Session);
    }

    [Fact]
    public async Task Database_File_Is_Not_Ordinary_Sqlite_At_Rest()
    {
        var (directory, _, session) = await CreateVaultAsync();
        try
        {
            session.Lock(); // release the file handle before reading the raw bytes
            var dbBytes = await File.ReadAllBytesAsync(Path.Combine(directory, "vault.db"));
            Assert.True(dbBytes.Length >= 16);
            Assert.False(
                Encoding.ASCII.GetString(dbBytes, 0, 15).StartsWith("SQLite format 3", StringComparison.Ordinal),
                "vault.db exposes a plaintext SQLite header — encryption is OFF");
        }
        finally
        {
            session.Dispose();
        }
    }

    [Fact]
    public async Task No_Plaintext_Secrets_In_Any_Vault_File()
    {
        var (directory, recoveryEncoded, session) = await CreateVaultAsync();
        try
        {
            session.Lock();

            var needles = new[]
            {
                Encoding.UTF8.GetBytes(MasterPassword),
                Encoding.Unicode.GetBytes(MasterPassword),
                Encoding.UTF8.GetBytes(recoveryEncoded),
                Encoding.UTF8.GetBytes(recoveryEncoded.Replace("-", string.Empty)),
            };

            foreach (var fileName in new[] { "header.bin", "vault.db", "vault.db-wal", "vault.db-shm", "vaults.json" })
            {
                var path = fileName == "vaults.json"
                    ? Path.Combine(_root, "vaults.json")
                    : Path.Combine(directory, fileName);
                if (!File.Exists(path))
                {
                    continue;
                }

                var bytes = await File.ReadAllBytesAsync(path);
                foreach (var needle in needles)
                {
                    Assert.False(SearchBytes(bytes, needle), $"secret material found in vault file");
                }
            }
        }
        finally
        {
            session.Dispose();
        }
    }

    [Fact]
    public async Task Database_Cannot_Be_Opened_Without_The_Key()
    {
        var (directory, _, session) = await CreateVaultAsync();
        try
        {
            session.Lock();
            var dbPath = Path.Combine(directory, "vault.db");

            // Unkeyed open on the SQLCipher provider must not yield readable rows.
            if (SQLitePCL.raw.sqlite3_open_v2(dbPath, out var db, SQLitePCL.raw.SQLITE_OPEN_READONLY, null)
                == SQLitePCL.raw.SQLITE_OK)
            {
                try
                {
                    Assert.NotEqual(SQLitePCL.raw.SQLITE_ROW, Step(db, "SELECT count(*) FROM app_meta;"));
                }
                finally
                {
                    SQLitePCL.raw.sqlite3_close_v2(db);
                }
            }
        }
        finally
        {
            session.Dispose();
        }
    }

    private static int Step(SQLitePCL.sqlite3 db, string sql)
    {
        if (SQLitePCL.raw.sqlite3_prepare_v2(db, sql, out var stmt) != SQLitePCL.raw.SQLITE_OK)
        {
            return -1;
        }

        try
        {
            return SQLitePCL.raw.sqlite3_step(stmt);
        }
        finally
        {
            SQLitePCL.raw.sqlite3_finalize(stmt);
        }
    }

    [Fact]
    public async Task Header_Tampering_Fails_Unlock()
    {
        var service = Service();
        var (directory, _, session) = await CreateVaultAsync();
        session.Dispose();
        var headerPath = Path.Combine(directory, "header.bin");
        var original = await File.ReadAllBytesAsync(headerPath);

        // Tamper each of the security-relevant regions; every variant must refuse to unlock.
        foreach (var (name, offset) in new[]
                 {
                     ("password wrap", 0x054),
                     ("recovery wrap", 0x090),
                     ("salt", 0x034),
                     ("kdf memory", 0x024),
                     ("header mac", 0x100),
                 })
        {
            var tampered = (byte[])original.Clone();
            tampered[offset] ^= 0x01;
            await File.WriteAllBytesAsync(headerPath, tampered);

            var failure = await Record.ExceptionAsync(
                () => service.UnlockWithPasswordAsync(directory, MasterPassword.ToCharArray()));
            Assert.True(failure is CryptoAuthenticationException or CryptoFormatException,
                $"Tampering {name} must fail authentication or format validation, got {failure?.GetType().Name}");
            _output.WriteLine($"tamper {name}: {failure?.GetType().Name} ✓");
        }

        await File.WriteAllBytesAsync(headerPath, original);
        using var healed = await service.UnlockWithPasswordAsync(directory, MasterPassword.ToCharArray());
        Assert.Equal(VaultSessionState.Unlocked, healed.State);
    }

    [Fact]
    public async Task Malformed_Headers_Are_Rejected_With_Typed_Errors_Never_Crashes()
    {
        var service = Service();
        var (directory, _, session) = await CreateVaultAsync();
        session.Dispose();
        var headerPath = Path.Combine(directory, "header.bin");
        var original = await File.ReadAllBytesAsync(headerPath);

        // Truncations at many lengths + deterministic single-bit mutations: only typed crypto
        // errors may escape (format/auth/unsupported), never ArgumentException/IndexOutOfRange/
        // OutOfMemory or any other runtime failure.
        var valid = HeaderCodec.Parse(original);

        foreach (var length in new[] { 0, 1, 100, 255, 256, 511 })
        {
            var cut = original.AsSpan(0, length).ToArray();
            var ex = Record.Exception(() => HeaderCodec.Parse(cut));
            Assert.True(ex is CryptoFormatException or UnsupportedFormatException,
                $"Truncated header ({length} B) must be a typed format error, got {ex?.GetType().Name}");
        }

        var rnd = new Random(20261022); // deterministic seed (ST-FZ-1 corpus)
        for (var round = 0; round < 2000; round++)
        {
            var mutated = (byte[])original.Clone();
            mutated[rnd.Next(mutated.Length)] ^= (byte)(1 << rnd.Next(8));

            var ex = Record.Exception(() => HeaderCodec.Parse(mutated));
            Assert.True(ex is null or CryptoException,
                $"Mutation at round {round} produced {ex?.GetType().Name}: {ex?.Message}");
        }

        Assert.Equal(CryptoFormat.HeaderFormatVersion, valid.HeaderFormatVersion);
        _ = directory; // vault left on disk; disposed with _root
    }

    private static bool SearchBytes(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length && match; j++)
            {
                match = haystack[i + j] == needle[j];
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
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
