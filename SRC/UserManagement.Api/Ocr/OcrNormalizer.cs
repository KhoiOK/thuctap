using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using UserManagement.Api.Entities;
using UserManagement.Api.Validators;

namespace UserManagement.Api.Ocr;

/// <summary>
/// Normalizes the raw values extracted by <see cref="OcrFieldExtractor"/> and validates
/// them against the existing business rules. A field that fails validation is still
/// returned - the UI then shows the matching error instead of silently correcting the
/// value (BR-14).
/// </summary>
public partial class OcrNormalizer
{
    private readonly OcrOptions _options;
    private readonly TimeProvider _time;

    public OcrNormalizer(IOptions<OcrOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
    }

    /// <summary>
    /// Normalizes every extracted field for the given document type.
    /// </summary>
    public Dictionary<string, OcrField> Normalize(
        IReadOnlyDictionary<string, ExtractedValue> extracted,
        DocumentType documentType,
        IReadOnlyDictionary<string, double>? confidences = null)
    {
        var result = new Dictionary<string, OcrField>(StringComparer.Ordinal);
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);

        foreach (var (key, extractedValue) in extracted)
        {
            var confidence = confidences is not null && confidences.TryGetValue(key, out var c) ? c : 1.0;
            var field = key switch
            {
                "documentNumber" => NormalizeDocumentNumber(extractedValue.Value, documentType, confidence, extractedValue.Source),
                "fullName" => NormalizeFullName(extractedValue.Value, confidence, extractedValue.Source),
                "dateOfBirth" => NormalizeDate(extractedValue.Value, confidence, today, extractedValue.Source),
                "gender" => NormalizeGender(extractedValue.Value, confidence, extractedValue.Source),
                "address" => NormalizeAddress(extractedValue.Value, confidence, extractedValue.Source),
                "issueDate" => NormalizeDate(extractedValue.Value, confidence, today, extractedValue.Source),
                "expiryDate" => NormalizeDate(extractedValue.Value, confidence, today, extractedValue.Source),
                "authorityNo" => NormalizeAuthorityNo(extractedValue.Value, confidence, extractedValue.Source),
                _ => new OcrField(null, confidence, extractedValue.Source, false)
            };

            result[key] = field;
        }

        return result;
    }

    private static OcrField NormalizeDocumentNumber(string raw, DocumentType documentType, double confidence, OcrFieldSource source)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());

        // Keep only the expected length: CCCD has 12 digits, CMND 9 or 12.
        var expected = documentType switch
        {
            DocumentType.CCCD => 12,
            DocumentType.CMND => 12,
            _ => 0
        };

        if (expected > 0 && digits.Length > expected)
        {
            digits = digits[^expected..];
        }
        else if (expected == 12 && documentType == DocumentType.CMND && digits.Length > 9 && digits.Length < 12)
        {
            // keep as-is
        }

        var isValid = NormalizationHelpers.IsValidDocumentNumber(documentType, digits);
        return new OcrField(string.IsNullOrEmpty(digits) ? null : digits, confidence, source, isValid);
    }

    private static OcrField NormalizeFullName(string raw, double confidence, OcrFieldSource source)
    {
        var cleaned = OcrFieldExtractor.CleanValue(raw);
        var collapsed = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var isValid = collapsed.Length is >= 2 and <= 100;
        return new OcrField(collapsed, confidence, source, isValid);
    }

    private static OcrField NormalizeGender(string raw, double confidence, OcrFieldSource source)
    {
        var value = OcrFieldExtractor.RemoveDiacritics(raw.ToLowerInvariant()).Trim();

        var gender = value switch
        {
            var v when v.StartsWith("nam") || v.Contains("male") => Gender.Male,
            var v when v.StartsWith("nu") || v.StartsWith("nữ") || v.Contains("female") => Gender.Female,
            var v when v.StartsWith("khac") || v.Contains("other") => Gender.Other,
            _ => (Gender?)null
        };

        return new OcrField(gender?.ToString(), confidence, source, gender is not null);
    }

    private static OcrField NormalizeAddress(string raw, double confidence, OcrFieldSource source)
    {
        var cleaned = OcrFieldExtractor.CleanValue(raw);
        var truncated = cleaned.Length > 500 ? cleaned[..500] : cleaned;
        var isValid = truncated.Length > 0;
        return new OcrField(truncated, confidence, source, isValid);
    }

    private static OcrField NormalizeAuthorityNo(string raw, double confidence, OcrFieldSource source)
    {
        var cleaned = OcrFieldExtractor.CleanValue(raw);
        return new OcrField(cleaned.Length == 0 ? null : cleaned, confidence, source, cleaned.Length > 0);
    }

    private OcrField NormalizeDate(string raw, double confidence, DateOnly today, OcrFieldSource source)
    {
        var parsed = TryParseDate(raw, today, out var inferred);

        if (parsed is null)
        {
            return new OcrField(null, confidence, source, false);
        }

        // An issued date in the future or an expiry date in the past is still a "readable"
        // value - the business validation reports it, not the OCR layer.
        //
        // The side is taken from where the value was actually found: the expiry date is
        // printed on the BACK of a CCCD, the date of birth on the FRONT. Hard-coding
        // "Front" here would report every date as coming from the front.
        return new OcrField(
            parsed.Value.ToString("yyyy-MM-dd"),
            confidence,
            source,
            true,
            inferred);
    }
    /// <summary>
    /// Parses a date written as <c>dd/MM/yyyy</c>, <c>dd-MM-yyyy</c> or <c>dd.MM.yyyy</c>.
    /// A two-digit year is expanded to 2000+yy (or 1900+yy when that would be in the
    /// future), which is flagged as inferred so the UI marks it "needs review".
    /// </summary>
    private DateOnly? TryParseDate(string raw, DateOnly today, out bool inferred)
    {
        inferred = false;

        var cleaned = OcrFieldExtractor.CleanValue(raw);
        var match = DateRegex().Match(cleaned);
        if (!match.Success)
        {
            return null;
        }

        if (!int.TryParse(match.Groups[1].Value, out var day)
            || !int.TryParse(match.Groups[2].Value, out var month))
        {
            return null;
        }

        var yearText = match.Groups[3].Value;
        if (!int.TryParse(yearText, out var year))
        {
            return null;
        }

        if (yearText.Length == 2)
        {
            if (!_options.AllowTwoDigitYearInference)
            {
                return null;
            }

            inferred = true;
            // A two-digit year is ambiguous: 20 -> 2020, 95 -> 1995. Prefer the century
            // that puts the date in the past.
            var candidate = 2000 + year;
            if (candidate > today.Year)
            {
                candidate = 1900 + year;
            }
            year = candidate;
        }

        if (year < 1900 || year > today.Year + 20)
        {
            return null;
        }

        try
        {
            return new DateOnly(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"(?<!\d)(?<d1>\d{1,2})[/\-.](?<m1>\d{1,2})[/\-.](?<y1>\d{2,4})(?!\d)")]
    private static partial Regex DateRegex();
}
