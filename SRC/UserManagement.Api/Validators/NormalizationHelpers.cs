using System.Globalization;
using System.Text.RegularExpressions;

namespace UserManagement.Api.Validators;

/// <summary>
/// Helpers implementing the BR-* normalization and validation rules. These are pure
/// functions so they can be unit-tested independently of the database.
/// </summary>
public static partial class NormalizationHelpers
{
    // BR-02: Vietnamese mobile numbers start with 03, 05, 07, 08, 09 followed by 8 digits.
    [GeneratedRegex(@"^0[35789][0-9]{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();

    public static string NormalizeEmail(string? email)
    {
        if (email is null)
        {
            return string.Empty;
        }
        return email.Trim().ToLowerInvariant();
    }

    public static string NormalizePhoneNumber(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }

        var trimmed = phone.Trim();
        // Drop spaces, dots, dashes per BR-02.
        var cleaned = new string(trimmed
            .Where(ch => ch is not (' ' or '.' or '-'))
            .ToArray());

        // Convert leading +84 or 84 to 0.
        if (cleaned.StartsWith("+84", StringComparison.Ordinal))
        {
            cleaned = "0" + cleaned[3..];
        }
        else if (cleaned.StartsWith("84", StringComparison.Ordinal) && cleaned.Length > 9)
        {
            cleaned = "0" + cleaned[2..];
        }

        return cleaned;
    }

    public static bool IsValidPhoneNumber(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return false;
        }
        return PhoneRegex().IsMatch(phone);
    }

    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }
        var trimmed = email.Trim();
        if (trimmed.Length > 255)
        {
            return false;
        }
        // .NET's MailAddress is forgiving - we re-validate the structural parts explicitly.
        var at = trimmed.IndexOf('@');
        if (at <= 0 || at == trimmed.Length - 1)
        {
            return false;
        }
        var local = trimmed[..at];
        var domain = trimmed[(at + 1)..];
        if (local.Length == 0 || domain.Length == 0 || !domain.Contains('.'))
        {
            return false;
        }
        return MailAddressTryCreate(trimmed);
    }

    private static bool MailAddressTryCreate(string value)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(value);
            return addr.Address == value;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsValidDateOfBirth(DateOnly? dob, DateOnly today)
    {
        if (dob is null)
        {
            return true; // optional
        }
        var min = new DateOnly(1900, 1, 1);
        return dob >= min && dob <= today;
    }

    // BR-08: identity document number rules.
    // CCCD  : exactly 12 digits.
    // CMND  : 9 or 12 digits.
    // Passport: 6-9 alphanumeric, uppercase.
    [GeneratedRegex(@"^\d{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex CccdNumberRegex();

    [GeneratedRegex(@"^(\d{9}|\d{12})$", RegexOptions.CultureInvariant)]
    private static partial Regex CmndNumberRegex();

    [GeneratedRegex(@"^[A-Z0-9]{6,9}$", RegexOptions.CultureInvariant)]
    private static partial Regex PassportNumberRegex();

    /// <summary>
    /// Normalizes a document number for the given type. Trims whitespace and uppercases
    /// passport numbers. Returns the input unchanged for CCCD/CMND (digits only).
    /// </summary>
    public static string NormalizeDocumentNumber(Entities.DocumentType type, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        return type switch
        {
            Entities.DocumentType.Passport => trimmed.ToUpperInvariant(),
            _ => trimmed
        };
    }

    /// <summary>
    /// Validates that <paramref name="value"/> matches the BR-08 rules for the given type.
    /// </summary>
    public static bool IsValidDocumentNumber(Entities.DocumentType type, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = NormalizeDocumentNumber(type, value);
        return type switch
        {
            Entities.DocumentType.CCCD => CccdNumberRegex().IsMatch(normalized),
            Entities.DocumentType.CMND => CmndNumberRegex().IsMatch(normalized),
            Entities.DocumentType.Passport => PassportNumberRegex().IsMatch(normalized),
            _ => false
        };
    }
}
