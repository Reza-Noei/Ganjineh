using System.Numerics;
using System.Security.Cryptography;

namespace Ganjineh.Cryptography.Generation;

/// <summary>Generator presets (Phase 5 contract §3.2).</summary>
public enum PasswordPreset
{
    Custom = 0,
    Standard = 1,
    Strong = 2,
    MaximumCompatibility = 3,
    NumericPin = 4,
}

/// <summary>Character-class flags for character-based generation.</summary>
[Flags]
public enum CharacterClasses
{
    None = 0,
    Lowercase = 1,
    Uppercase = 2,
    Digits = 4,
    Symbols = 8,
}

/// <summary>
/// Options for the character-based generator. All inputs are validated before generation;
/// impossible combinations throw <see cref="GenerationException"/>.
/// </summary>
public sealed record PasswordGenerationOptions
{
    public const int MinLength = 4;
    public const int MaxLength = 256;

    public int Length { get; init; } = 20;
    public CharacterClasses Classes { get; init; } =
        CharacterClasses.Lowercase | CharacterClasses.Uppercase | CharacterClasses.Digits;
    public bool ExcludeAmbiguous { get; init; }
    public string? ExcludedCharacters { get; init; }
    public int MinLowercase { get; init; }
    public int MinUppercase { get; init; }
    public int MinDigits { get; init; }
    public int MinSymbols { get; init; }

    public static PasswordGenerationOptions ForPreset(PasswordPreset preset) => preset switch
    {
        PasswordPreset.Standard => new()
        {
            Length = 16,
            Classes = CharacterClasses.Lowercase | CharacterClasses.Uppercase | CharacterClasses.Digits,
        },
        PasswordPreset.Strong => new()
        {
            Length = 24,
            Classes = CharacterClasses.Lowercase | CharacterClasses.Uppercase | CharacterClasses.Digits
                | CharacterClasses.Symbols,
        },
        PasswordPreset.MaximumCompatibility => new()
        {
            Length = 20,
            Classes = CharacterClasses.Lowercase | CharacterClasses.Uppercase | CharacterClasses.Digits,
            ExcludeAmbiguous = true,
        },
        PasswordPreset.NumericPin => new() { Length = 8, Classes = CharacterClasses.Digits },
        _ => new(),
    };
}

/// <summary>A generated password with its uniform-sampling entropy estimate.</summary>
public sealed record GeneratedPassword(string Value, double EntropyBits);

/// <summary>Thrown for impossible or out-of-range generator configurations.</summary>
public sealed class GenerationException(string messageKey) : Exception(messageKey);

/// <summary>
/// Cryptographically secure character-based password generator (password-generator.md).
/// Unbiased selection: every random choice uses RandomNumberGenerator.GetInt32 (rejection
/// sampling — no modulo bias). Category minimums are satisfied by drawing the required counts
/// per class, filling the remainder from the union pool, then applying an unbiased Fisher–Yates
/// shuffle — the result is a uniform random permutation of a uniform random multiset that
/// satisfies the constraints.
/// </summary>
public interface IPasswordGenerator
{
    GeneratedPassword Generate(PasswordGenerationOptions options);
}

public sealed class PasswordGenerator : IPasswordGenerator
{
    private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digits = "0123456789";
    private const string Symbols = "!@#$%^&*()-_=+[]{};:,.?/";

    private const string AmbiguousLower = "il1|";
    private const string AmbiguousUpper = "ILO0";
    private const string AmbiguousDigits = "01";
    private const string AmbiguousSymbols = "{}[]()/\\'\"`~,;:.<>|";

    public GeneratedPassword Generate(PasswordGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Length is < PasswordGenerationOptions.MinLength or > PasswordGenerationOptions.MaxLength)
        {
            throw new GenerationException("GeneratorLength");
        }

        if (options.Classes == CharacterClasses.None)
        {
            throw new GenerationException("GeneratorNoClasses");
        }

        var classPools = new List<(int MinCount, string Pool)>();
        var union = new System.Text.StringBuilder();

        void AddClass(CharacterClasses flag, string source, string ambiguous, int minCount)
        {
            var enabled = (options.Classes & flag) != 0;
            var pool = enabled
                ? new string(source
                    .Where(c => !options.ExcludeAmbiguous || !ambiguous.Contains(c))
                    .Where(c => options.ExcludedCharacters?.Contains(c) != true)
                    .ToArray())
                : string.Empty;

            if (!enabled)
            {
                if (minCount > 0)
                {
                    throw new GenerationException("GeneratorImpossible");
                }

                return;
            }

            if (pool.Length == 0)
            {
                throw minCount > 0
                    ? new GenerationException("GeneratorImpossible")
                    : new GenerationException("GeneratorEmptyClass");
            }

            classPools.Add((minCount, pool));
            union.Append(pool);
        }

        AddClass(CharacterClasses.Lowercase, Lowercase, AmbiguousLower, options.MinLowercase);
        AddClass(CharacterClasses.Uppercase, Uppercase, AmbiguousUpper, options.MinUppercase);
        AddClass(CharacterClasses.Digits, Digits, AmbiguousDigits, options.MinDigits);
        AddClass(CharacterClasses.Symbols, Symbols, AmbiguousSymbols, options.MinSymbols);

        var poolText = union.ToString();
        var minTotal = options.MinLowercase + options.MinUppercase + options.MinDigits + options.MinSymbols;
        if (minTotal > options.Length)
        {
            throw new GenerationException("GeneratorImpossible");
        }

        var chars = new char[options.Length];

        // 1. Satisfy per-class minimums with unbiased draws from each class pool.
        var position = 0;
        foreach (var (minCount, pool) in classPools)
        {
            for (var i = 0; i < minCount; i++)
            {
                chars[position++] = pool[RandomNumberGenerator.GetInt32(pool.Length)];
            }
        }

        // 2. Fill the remainder from the union pool.
        for (; position < options.Length; position++)
        {
            chars[position] = poolText[RandomNumberGenerator.GetInt32(poolText.Length)];
        }

        // 3. Unbiased Fisher–Yates shuffle.
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        // Entropy: uniform sampling from pool^length. Per-class minimums reduce the valid space
        // slightly; the reported value is the conservative unconstrained estimate, documented in
        // password-generator.md.
        var entropy = options.Length * BigInteger.Log(poolText.Length, 2);
        return new GeneratedPassword(new string(chars), Math.Round(entropy, 1));
    }
}
