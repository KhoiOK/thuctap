using System.Text;

namespace UserManagement.Api.Entities;

/// <summary>
/// User aggregate root. Holds normalized data only - the source values are validated and
/// converted (lowercase email, normalized phone, FullNameSearch without diacritics) before
/// being persisted. Soft-delete is implemented via <see cref="IsDeleted"/> plus a
/// global query filter in <c>ApplicationDbContext</c>.
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Original full name as provided (already trimmed).</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Search-friendly variant of <see cref="FullName"/>: lowercased, no Vietnamese
    /// diacritics, multiple whitespace collapsed. Computed in <c>UserService</c>.
    /// </summary>
    public string FullNameSearch { get; set; } = string.Empty;

    /// <summary>Email stored in lower case (BR-01).</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Phone number stored after BR-02 normalization.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    public DateOnly? DateOfBirth { get; set; }

    public Gender? Gender { get; set; }

    public string? Address { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public ICollection<IdentityDocument> IdentityDocuments { get; set; } = new List<IdentityDocument>();

    /// <summary>
    /// Build the search-friendly variant of <paramref name="fullName"/>.
    /// Implements the rules in Database-Design §2.1.
    /// </summary>
    public static string BuildFullNameSearch(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return string.Empty;
        }

        var trimmed = string.Join(' ', fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return RemoveDiacritics(trimmed).ToLowerInvariant();
    }

    private static string RemoveDiacritics(string input)
    {
        // Normalize to FormD (decomposed) so combining marks become separate code points,
        // then strip the combining marks. Also map "đ"/"Đ" to "d".
        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString()
                 .Replace('đ', 'd')
                 .Replace('Đ', 'd')
                 .Normalize(NormalizationForm.FormC);
    }
}
