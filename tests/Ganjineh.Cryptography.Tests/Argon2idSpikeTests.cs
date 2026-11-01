using System.Diagnostics;
using NSec.Cryptography;
using Xunit;
using Xunit.Abstractions;

namespace Ganjineh.Cryptography.Tests;

/// <summary>Q1 spike: verify NSec/libsodium Argon2id runs on this platform before building on it.</summary>
public class Argon2idSpikeTests
{
    private readonly ITestOutputHelper _output;

    public Argon2idSpikeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Argon2id_Derives_On_This_Platform()
    {
        // NSec 26 (libsodium crypto_pwhash) supports DegreeOfParallelism = 1 only; the versioned
        // KDF metadata keeps the parallelism field for future engines (documented ADR-003 deviation).
        var algorithm = new Argon2id(new Argon2Parameters
        {
            MemorySize = 65536,
            NumberOfPasses = 3,
            DegreeOfParallelism = 1,
        });

        var password = System.Text.Encoding.UTF8.GetBytes("spike-رمز-🔒");
        var salt = new byte[16];

        var sw = Stopwatch.StartNew();
        var derived = algorithm.DeriveBytes(password, salt, 32);
        sw.Stop();

        Assert.Equal(32, derived.Length);
        Assert.Contains(derived, b => b != 0);
        _output.WriteLine($"Argon2id 64MiB/3passes/p=4 took {sw.ElapsedMilliseconds} ms");
    }
}
