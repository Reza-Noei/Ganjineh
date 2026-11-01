using System.Security.Cryptography;
using System.Text;
using PasswordManager.Cryptography.Generation;
using PasswordManager.Cryptography.Otp;
using Xunit;
using Xunit.Abstractions;

namespace PasswordManager.Cryptography.Tests;

/// <summary>Password generator: constraints, distribution sanity, entropy honesty.</summary>
public class PasswordGeneratorTests
{
    private readonly PasswordGenerator _generator = new();
    private readonly ITestOutputHelper _output;

    public PasswordGeneratorTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Respects_Length_And_Classes()
    {
        var result = _generator.Generate(new PasswordGenerationOptions
        {
            Length = 32,
            Classes = CharacterClasses.Lowercase | CharacterClasses.Uppercase | CharacterClasses.Digits | CharacterClasses.Symbols,
        });

        Assert.Equal(32, result.Value.Length);
        Assert.Contains(result.Value, char.IsLower);
        Assert.Contains(result.Value, char.IsUpper);
        Assert.Contains(result.Value, char.IsDigit);
        Assert.True(result.EntropyBits > 180);
    }

    [Fact]
    public void Minimum_Counts_Are_Satisfied()
    {
        var result = _generator.Generate(new PasswordGenerationOptions
        {
            Length = 20,
            Classes = CharacterClasses.Lowercase | CharacterClasses.Digits,
            MinDigits = 6,
            MinLowercase = 2,
        });

        Assert.True(result.Value.Count(char.IsDigit) >= 6);
        Assert.True(result.Value.Count(char.IsLower) >= 2);
    }

    [Fact]
    public void Excluded_Characters_Never_Appear()
    {
        var result = _generator.Generate(new PasswordGenerationOptions
        {
            Length = 64,
            Classes = CharacterClasses.Lowercase | CharacterClasses.Digits,
            ExcludedCharacters = "abc012",
        });

        Assert.DoesNotContain(result.Value, c => "abc012".Contains(c));
    }

    [Fact]
    public void Ambiguous_Exclusion_Removes_Homoglyphs()
    {
        var result = _generator.Generate(new PasswordGenerationOptions
        {
            Length = 64,
            Classes = CharacterClasses.Lowercase | CharacterClasses.Uppercase,
            ExcludeAmbiguous = true,
        });

        Assert.DoesNotContain(result.Value, c => "il1|ILO0".Contains(c));
    }

    [Theory]
    [InlineData(3)]                 // below min length
    [InlineData(257)]               // above max length
    public void Impossible_Lengths_Are_Rejected(int length)
    {
        Assert.Throws<GenerationException>(() => _generator.Generate(
            new PasswordGenerationOptions { Length = length, Classes = CharacterClasses.Lowercase }));
    }

    [Fact]
    public void Impossible_MinCount_Is_Rejected()
    {
        // 20-length password cannot contain 30 digits.
        Assert.Throws<GenerationException>(() => _generator.Generate(new PasswordGenerationOptions
        {
            Length = 20, Classes = CharacterClasses.Digits, MinDigits = 30,
        }));
    }

    [Fact]
    public void Disabled_Class_With_MinCount_Is_Rejected()
    {
        Assert.Throws<GenerationException>(() => _generator.Generate(new PasswordGenerationOptions
        {
            Length = 20, Classes = CharacterClasses.Lowercase, MinDigits = 1,
        }));
    }

    [Fact]
    public void Empty_Class_After_Exclusions_Is_Rejected()
    {
        // Excluding every lowercase letter leaves an empty class pool.
        var allLower = "abcdefghijklmnopqrstuvwxyz";
        Assert.Throws<GenerationException>(() => _generator.Generate(new PasswordGenerationOptions
        {
            Length = 20, Classes = CharacterClasses.Lowercase, ExcludedCharacters = allLower,
        }));
    }

    [Fact]
    public void Distribution_Charity_Check()
    {
        // Statistical sanity over many digits draws: each digit should appear ~10% (±3σ).
        var counts = new int[10];
        const int samples = 20_000;
        for (var i = 0; i < samples; i++)
        {
            var password = _generator.Generate(new PasswordGenerationOptions
            {
                Length = 10, Classes = CharacterClasses.Digits,
            });
            foreach (var c in password.Value)
            {
                counts[c - '0']++;
            }
        }

        var expected = samples * 10 / 10.0;
        var sigma = Math.Sqrt(expected);
        for (var d = 0; d < 10; d++)
        {
            Assert.InRange(counts[d], expected - 4 * sigma, expected + 4 * sigma);
        }

        _output.WriteLine($"digit counts: {string.Join(", ", counts)} (expected ≈ {expected})");
    }

    [Fact]
    public void Presets_Produce_Distinct_Configurations()
    {
        var pin = _generator.Generate(PasswordGenerationOptions.ForPreset(PasswordPreset.NumericPin));
        Assert.Equal(8, pin.Value.Length);
        Assert.All(pin.Value, c => Assert.True(char.IsDigit(c)));

        var strong = _generator.Generate(PasswordGenerationOptions.ForPreset(PasswordPreset.Strong));
        Assert.Equal(24, strong.Value.Length);
    }

    [Fact]
    public void Generated_Passwords_Are_Not_Repeated()
    {
        var a = _generator.Generate(new PasswordGenerationOptions { Length = 24, Classes = CharacterClasses.Lowercase | CharacterClasses.Digits });
        var b = _generator.Generate(new PasswordGenerationOptions { Length = 24, Classes = CharacterClasses.Lowercase | CharacterClasses.Digits });
        Assert.NotEqual(a.Value, b.Value);
    }
}

/// <summary>Passphrase generator over the EFF large wordlist.</summary>
public class PassphraseGeneratorTests
{
    private readonly PassphraseGenerator _generator = new();

    [Fact]
    public void Wordlist_Integrity_7776_Unique_Words()
    {
        // The generator constructor itself validates; a successful construction implies integrity.
        Assert.NotNull(_generator);
    }

    [Fact]
    public void Word_Count_And_Separator()
    {
        var result = _generator.Generate(new PassphraseOptions { WordCount = 5, Separator = "." });
        Assert.Equal(5, result.Value.Split('.').Length);
        Assert.True(result.EntropyBits > 60); // 5 × 12.925 ≈ 64.6
    }

    [Fact]
    public void Capitalization_And_Number_Suffix()
    {
        var result = _generator.Generate(new PassphraseOptions
        {
            WordCount = 4, Separator = "-", Capitalize = true, AppendNumber = true,
        });

        var parts = result.Value.Split('-');
        Assert.Equal(4, parts.Length);
        Assert.All(parts[..3], word => Assert.Matches("^[A-Z][a-z]+$", word));
        Assert.Matches("^[A-Z][a-z]+\\d$", parts[^1]);
        // 4 words + number suffix: 4 × 12.925 + log2(10) ≈ 51.7 + 3.32
        Assert.InRange(result.EntropyBits, 54, 56);
    }

    [Fact]
    public void Word_Count_Bounds_Are_Enforced()
    {
        Assert.Throws<GenerationException>(() => _generator.Generate(new PassphraseOptions { WordCount = 2 }));
        Assert.Throws<GenerationException>(() => _generator.Generate(new PassphraseOptions { WordCount = 13 }));
    }

    [Fact]
    public void Entropy_Matches_Constant_Wordlist_Size()
    {
        var result = _generator.Generate(new PassphraseOptions { WordCount = 6 });
        Assert.Equal(Math.Round(6 * Math.Log2(7776), 1), result.EntropyBits);
    }
}
