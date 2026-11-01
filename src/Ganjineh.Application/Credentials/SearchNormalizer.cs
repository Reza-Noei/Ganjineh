using System.Globalization;
using System.Text;

namespace Ganjineh.Application.Credentials;

/// <summary>
/// Persian/Arabic-aware search normalization (search-design.md): folds Arabic character variants
/// to their Persian forms, strips diacritics and invisible marks, lowercases. Applied identically
/// to indexed text and queries — normalization is never persisted.
/// </summary>
public static class SearchNormalizer
{
    public static string Normalize(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            switch (ch)
            {
                // Arabic Yeh / Alef Maksura → Persian Yeh; Arabic Kaf → Persian Kaf.
                case 'ي': sb.Append('ی'); break;   // ي → ی
                case 'ى': sb.Append('ی'); break;   // ى → ی
                case 'ك': sb.Append('ک'); break;   // ك → ک
                // Alef variants → bare alef.
                case 'أ' or 'إ' or 'آ': sb.Append('ا'); break; // أ إ آ → ا
                // Taa marbuta → heh.
                case 'ة': sb.Append('ه'); break;   // ة → ه
                // Diacritics (harakat) and superscript alef: drop.
                case >= 'ً' and <= 'ٟ':
                case 'ٰ':
                // Invisible direction/Joiner marks: drop.
                case '‌':
                case '‍':
                case '‎':
                case '‏':
                    break;
                default:
                    sb.Append(char.ToLowerInvariant(ch));
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>True when every whitespace-separated query token occurs in the normalized text.</summary>
    public static bool Matches(string? text, string normalizedQuery)
    {
        if (normalizedQuery.Length == 0)
        {
            return true;
        }

        var normalizedText = Normalize(text);
        foreach (var token in normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!normalizedText.Contains(token, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
