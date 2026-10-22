using System.Security.Cryptography;

namespace PasswordManager.Cryptography.Generation;

/// <summary>Passphrase options.</summary>
public sealed record PassphraseOptions
{
    public const int MinWords = 3;
    public const int MaxWords = 12;

    public int WordCount { get; init; } = 5;
    public string Separator { get; init; } = "-";
    public bool Capitalize { get; init; }
    public bool AppendNumber { get; init; }
}

/// <summary>A generated passphrase with its entropy estimate.</summary>
public sealed record GeneratedPassphrase(string Value, double EntropyBits);

/// <summary>
/// Diceware-style passphrase generator over the EFF Large Wordlist (7,776 unique words, CC BY 4.0
/// — see Assets/eff_large_wordlist.txt + LICENSE note). Each word carries log2(7776) ≈ 12.925
/// bits; word indices are drawn with RandomNumberGenerator.GetInt32 (rejection sampling). A
/// numeric suffix adds 10 bits (two uniform digits appended to the final word).
/// </summary>
public interface IPassphraseGenerator
{
    GeneratedPassphrase Generate(PassphraseOptions options);
}

public sealed class PassphraseGenerator : IPassphraseGenerator
{
    private readonly string[] _words;

    public PassphraseGenerator()
    {
        var assembly = typeof(PassphraseGenerator).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "PasswordManager.Cryptography.Assets.eff_large_wordlist.txt")
            ?? throw new InvalidOperationException("EFF wordlist resource is missing.");
        using var reader = new StreamReader(stream);
        var words = new List<string>(7776);
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split('\t');
            if (parts.Length == 2)
            {
                var word = parts[1].Trim();
                // EFF large list: lowercase words, four of which are hyphenated (t-shirt, …).
                if (word.Length > 0 && word.All(c => char.IsLower(c) || c == '-'))
                {
                    words.Add(word);
                }
            }
        }

        if (words.Count != 7776 || words.Distinct().Count() != 7776)
        {
            throw new InvalidOperationException("EFF wordlist failed its integrity check (expected 7776 unique words).");
        }

        _words = [.. words];
    }

    public GeneratedPassphrase Generate(PassphraseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.WordCount is < PassphraseOptions.MinWords or > PassphraseOptions.MaxWords)
        {
            throw new GenerationException("GeneratorWordCount");
        }

        var words = new string[options.WordCount];
        for (var i = 0; i < options.WordCount; i++)
        {
            var word = _words[RandomNumberGenerator.GetInt32(_words.Length)];
            words[i] = options.Capitalize
                ? char.ToUpperInvariant(word[0]) + word[1..]
                : word;
        }

        double entropy = options.WordCount * Math.Log2(_words.Length);
        if (options.AppendNumber)
        {
            words[^1] += RandomNumberGenerator.GetInt32(10).ToString();
            entropy += Math.Log2(10);
        }

        return new GeneratedPassphrase(string.Join(options.Separator, words), Math.Round(entropy, 1));
    }
}
