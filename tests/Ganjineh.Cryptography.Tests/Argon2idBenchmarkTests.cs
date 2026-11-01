using System.Diagnostics;
using NSec.Cryptography;
using Ganjineh.Cryptography.Kdf;
using Xunit;
using Xunit.Abstractions;

namespace Ganjineh.Cryptography.Tests;

/// <summary>
/// Q2: Argon2id parameter benchmark (ADR-003 §3.1 procedure). Explicit-run only — excluded from
/// normal test runs to keep the suite fast: dotnet test --filter "Category=Benchmark".
/// </summary>
[Trait("Category", "Benchmark")]
public class Argon2idBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public Argon2idBenchmarkTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(32768, 3)]
    [InlineData(65536, 3)]
    [InlineData(131072, 3)]
    [InlineData(65536, 2)]
    public void Measure(long memoryKiB, int passes)
    {
        var algorithm = new Argon2id(new Argon2Parameters
        {
            MemorySize = memoryKiB,
            NumberOfPasses = passes,
            DegreeOfParallelism = 1,
        });

        var password = "benchmark-رمز-Password1!"u8.ToArray();
        var salt = "0123456789abcdef"u8.ToArray();

        // Warm-up.
        _ = algorithm.DeriveBytes(password, salt, 32);

        var samples = new List<long>();
        for (var i = 0; i < 20; i++)
        {
            var sw = Stopwatch.StartNew();
            _ = algorithm.DeriveBytes(password, salt, 32);
            samples.Add(sw.ElapsedMilliseconds);
        }

        samples.Sort();
        var median = samples[samples.Count / 2];
        var p95 = samples[(int)(samples.Count * 0.95)];

        _output.WriteLine($"Argon2id m={memoryKiB}KiB t={passes} p=1: median={median} ms, p95={p95} ms");
        Assert.InRange(p95, 1, 10_000);
    }
}
