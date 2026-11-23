using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace UserManagement.Api.Ocr;

/// <summary>
/// Extracts business fields from the raw OCR text. This is the single source of truth for
/// the label → field mapping (see <c>OCR-Cloudinary-Spec.md</c> §3.3). The Python service
/// only reads pixels; it does not understand Vietnamese identity documents.
/// </summary>
public static partial class OcrFieldExtractor
{
    /// <summary>
    /// Label aliases per business field. Matching ignores case, diacritics and
    /// punctuation differences.
    /// </summary>
    private static readonly Dictionary<string, string[]> LabelAliases = new(StringComparer.Ordinal)
    {
        ["documentNumber"] = ["so cccd", "so chung minh", "so cmnd", "so can cuoc", "so the can cuoc", "so"],
        ["fullName"] = ["ho va ten day du", "ho va ten", "ho ten"],
        ["dateOfBirth"] = ["ngay sinh"],
        ["gender"] = ["gioi tinh"],
        ["address"] = ["dia chi thuong tru", "dia chi"],
        ["issueDate"] = ["ngay cap"],
        ["expiryDate"] = ["co gia tri den ngay", "co gia tri den", "gia tri den ngay", "ngay het han"],
        ["authorityNo"] = ["so hieu", "co quan cap"]
    };

    /// <summary>Which side of the document each field lives on.</summary>
    private static readonly Dictionary<string, OcrFieldSource> FieldSources = new(StringComparer.Ordinal)
    {
        ["documentNumber"] = OcrFieldSource.Front,
        ["fullName"] = OcrFieldSource.Front,
        ["dateOfBirth"] = OcrFieldSource.Front,
        ["gender"] = OcrFieldSource.Front,
        ["address"] = OcrFieldSource.Front,
        ["issueDate"] = OcrFieldSource.Front,
        ["expiryDate"] = OcrFieldSource.Back,
        ["authorityNo"] = OcrFieldSource.Back
    };

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonAlphanumericRegex();

    /// <summary>
    /// Extracts the raw (un-normalized) value of every known field from the OCR text.
    /// </summary>
    /// <param name="rawText">Raw text per side, as returned by the engine.</param>
    /// <returns>
    /// Dictionary of field name → raw value. Only fields that were actually found are
    /// included.
    /// </returns>
    public static Dictionary<string, ExtractedValue> Extract(IReadOnlyDictionary<string, string>? rawText)
    {
        var result = new Dictionary<string, ExtractedValue>(StringComparer.Ordinal);
        if (rawText is null || rawText.Count == 0)
        {
            return result;
        }

        var frontLines = SplitLines(rawText.GetValueOrDefault("front"));
        var backLines = SplitLines(rawText.GetValueOrDefault("back"));

        foreach (var (field, aliases) in LabelAliases)
        {
            var source = FieldSources[field];
            var lines = source == OcrFieldSource.Front ? frontLines : backLines;

            var value = FindValue(lines, aliases);
            if (value is not null)
            {
                result[field] = new ExtractedValue(value, source);
            }
        }

        return result;
    }

    /// <summary>
    /// Finds the value that follows one of the given labels. Falls back to the next line
    /// when the layout puts the value on a separate line (multi-column cards).
    /// </summary>
    private static string? FindValue(IReadOnlyList<string> lines, IReadOnlyList<string> aliases)
    {
        foreach (var alias in aliases)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var labelEnd = FindLabelEnd(line, alias);
                if (labelEnd is null)
                {
                    continue;
                }

                // Value on the same line, after the label.
                var candidate = CleanValue(line[(labelEnd.Value)..]);
                if (candidate.Length > 0)
                {
                    return candidate;
                }

                // Value on the next line.
                if (i + 1 < lines.Count)
                {
                    var next = CleanValue(lines[i + 1]);
                    if (next.Length > 0 && !IsAnyLabelLine(next))
                    {
                        return next;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the line starts with <em>any</em> known label - not just the aliases of
    /// the field currently being extracted. Without this, a label line of a different
    /// field (e.g. "Ngày sinh: ..." following an empty "Họ và tên:") would be mistaken
    /// for the value of the current field.
    /// </summary>
    private static bool IsAnyLabelLine(string line)
    {
        foreach (var aliases in LabelAliases.Values)
        {
            if (IsLabelLine(line, aliases))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Finds the position right after the given label inside <paramref name="line"/>, or
    /// <c>null</c> when the label is not present. The comparison is diacritic- and
    /// case-insensitive, so the returned index refers to the original string.
    /// </summary>
    private static int? FindLabelEnd(string line, string alias)
    {
        var normalized = NormalizeWithMap(line);
        var normalizedAlias = NormalizeLabel(alias);

        if (normalizedAlias.Length == 0)
        {
            return null;
        }

        var index = normalized.Text.IndexOf(normalizedAlias, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        // The alias must be at the start of the line (or preceded only by punctuation /
        // whitespace) so "so" does not match inside an unrelated word.
        if (index > 0 && !char.IsWhiteSpace(normalized.Text[index - 1]))
        {
            var prefix = normalized.Text[..index].Trim();
            if (prefix.Length > 0)
            {
                return null;
            }
        }

        var lastOriginalIndex = normalized.Map[index + normalizedAlias.Length - 1];
        return lastOriginalIndex + 1;
    }

    private static bool IsLabelLine(string line, IReadOnlyList<string> aliases)
    {
        foreach (var alias in aliases)
        {
            if (FindLabelEnd(line, alias) is not null)
            {
                return true;
            }
        }
        return false;
    }

    private static List<string> SplitLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        return text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Normalizes a label for comparison: lowercase, diacritics removed, punctuation
    /// collapsed to single spaces.
    /// </summary>
    internal static string NormalizeLabel(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var lower = input.ToLowerInvariant();
        var noDiacritics = RemoveDiacritics(lower);
        var noPunctuation = NonAlphanumericRegex().Replace(noDiacritics, " ").Trim();
        return string.Join(' ', noPunctuation.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Normalizes a string while keeping a map back to the original indices, so a match
    /// found in the normalized text can be sliced out of the original string.
    /// </summary>
    private static NormalizedText NormalizeWithMap(string input)
    {
        var lower = input.ToLowerInvariant();
        var noDiacritics = RemoveDiacritics(lower);

        var sb = new StringBuilder(noDiacritics.Length);
        var map = new List<int>(noDiacritics.Length);

        for (var i = 0; i < noDiacritics.Length; i++)
        {
            var ch = noDiacritics[i];
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                map.Add(i);
            }
            else if (sb.Length > 0 && sb[^1] != ' ')
            {
                sb.Append(' ');
                map.Add(i);
            }
        }

        var text = sb.ToString().TrimEnd();
        return new NormalizedText(text, map);
    }

    /// <summary>
    /// Cleans an extracted value: removes trailing separators and collapses whitespace.
    /// </summary>
    internal static string CleanValue(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var trimmed = input.Trim().TrimStart(':', '-', '=', '.', ',').Trim();
        var sb = new StringBuilder(trimmed.Length);
        var lastWasSpace = false;
        foreach (var ch in trimmed)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                }
                lastWasSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
        }

        return sb.ToString().Trim().TrimEnd(':', '.', ',').Trim();
    }

    internal static string RemoveDiacritics(string input)
    {
        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString()
                 .Replace('đ', 'd')
                 .Replace('Đ', 'd')
                 .Normalize(NormalizationForm.FormC);
    }

    /// <summary>Normalized text plus the original index of each normalized character.</summary>
    private readonly record struct NormalizedText(string Text, List<int> Map);
}

/// <summary>A raw value found in the OCR text together with the side it came from.</summary>
public sealed record ExtractedValue(string Value, OcrFieldSource Source);
