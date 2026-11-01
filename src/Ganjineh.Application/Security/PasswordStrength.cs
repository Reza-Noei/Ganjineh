using Zxcvbn;

namespace Ganjineh.Application.Security;

/// <summary>Strength score bands (0–4, zxcvbn scale) with Persian-facing labels.</summary>
public enum StrengthScore
{
    VeryWeak = 0,
    Weak = 1,
    Fair = 2,
    Strong = 3,
    VeryStrong = 4,
}

/// <summary>Estimation result. Contains no raw password.</summary>
public sealed record PasswordStrengthResult(StrengthScore Score, double GuessesLog10, string? WarningKey, IReadOnlyList<string> SuggestionKeys)
{
    public static PasswordStrengthResult Empty { get; } = new(StrengthScore.VeryWeak, 0, null, []);
}

/// <summary>
/// Local password strength estimation (password-strength-estimation.md): wraps the zxcvbn-core
/// port (7.0.92, MIT) — matching, scoring and feedback run entirely offline. Scores are relative
/// resistance estimates, NOT cracking-time guarantees.
/// </summary>
public interface IPasswordStrengthEstimator
{
    PasswordStrengthResult Evaluate(ReadOnlySpan<char> password);
}

public sealed class ZxcvbnStrengthEstimator : IPasswordStrengthEstimator
{
    public PasswordStrengthResult Evaluate(ReadOnlySpan<char> password)
    {
        if (password.IsEmpty)
        {
            return PasswordStrengthResult.Empty;
        }

        var result = Core.EvaluatePassword(password.ToString());
        var warningKey = result.Feedback?.Warning is { Length: > 0 } warning
            ? MapWarning(warning)
            : null;
        var suggestionKeys = result.Feedback?.Suggestions is { Count: > 0 } suggestions
            ? suggestions.Select(_ => "Strength.Suggestion.ImproveLength").ToList()
            : [];
        return new PasswordStrengthResult(
            (StrengthScore)Math.Clamp(result.Score, 0, 4),
            Math.Log10(Math.Max(result.Guesses, 1)),
            warningKey,
            suggestionKeys);
    }

    /// <summary>
    /// zxcvbn warnings are English sentences; we map the known set onto localization keys so the
    /// UI speaks Persian. Unknown warnings fall back to the generic key.
    /// </summary>
    private static string MapWarning(string warning) => warning.Contains("dictionary", StringComparison.OrdinalIgnoreCase)
        ? "Strength.Warn.Dictionary"
        : warning.Contains("sequence", StringComparison.OrdinalIgnoreCase)
            ? "Strength.Warn.Sequence"
            : warning.Contains("repeat", StringComparison.OrdinalIgnoreCase)
                ? "Strength.Warn.Repeat"
                : warning.Contains("year", StringComparison.OrdinalIgnoreCase) || warning.Contains("date", StringComparison.OrdinalIgnoreCase)
                    ? "Strength.Warn.Date"
                    : "Strength.Warn.Common";
}

/// <summary>Localized security feedback keys for the estimator (fixed set):</summary>
public static class StrengthFeedbackKeys
{
    public const string GenericWarning = "Strength.Warn.Common";
    public const string GenericSuggestion = "Strength.Suggestion.ImproveLength";
}
