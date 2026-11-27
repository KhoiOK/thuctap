using UserManagement.Web.Services;

namespace UserManagement.Web.ViewModels.Users;

/// <summary>
/// SCR-07: state of the automatic identity-document scan inside the SCR-06 form.
/// </summary>
public class OcrScanViewModel
{
    /// <summary>Identifies the scan so the form can be confirmed later.</summary>
    public Guid ScanId { get; set; }

    /// <summary>The fields that were read, keyed by the form field name.</summary>
    public IReadOnlyDictionary<string, OcrFieldDto> Fields { get; set; } =
        new Dictionary<string, OcrFieldDto>();

    /// <summary>Warnings returned by the API (low confidence, invalid values, ...).</summary>
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();

    /// <summary>How long the scan took on the server (milliseconds).</summary>
    public int ProcessingTimeMs { get; set; }

    /// <summary>True once a scan has been performed successfully.</summary>
    public bool HasResult => Fields.Count > 0;

    /// <summary>
    /// Returns the value of a field when it may be auto-filled: the value must exist,
    /// pass the business validation and reach the minimum confidence (BR-14).
    /// </summary>
    public string? AutoFillValue(string fieldName, double minConfidence)
    {
        if (!Fields.TryGetValue(fieldName, out var field))
        {
            return null;
        }

        if (!field.HasValue || !field.IsValid)
        {
            return null;
        }

        if (field.Confidence < minConfidence)
        {
            return null;
        }

        return field.Value;
    }

    /// <summary>
    /// Returns the confidence of a field so the UI can show the matching badge.
    /// </summary>
    public double? ConfidenceOf(string fieldName)
        => Fields.TryGetValue(fieldName, out var field) ? field.Confidence : null;

    /// <summary>
    /// The confidence badge kind of a field: <c>high</c>, <c>medium</c>, <c>low</c> or
    /// <c>null</c> when the field was not read at all.
    /// </summary>
    public string? ConfidenceBadge(string fieldName, double minConfidence, double highConfidence)
    {
        if (!Fields.TryGetValue(fieldName, out var field))
        {
            return null;
        }

        if (!field.HasValue)
        {
            return "missing";
        }

        if (field.Confidence >= highConfidence)
        {
            return "high";
        }

        if (field.Confidence >= minConfidence)
        {
            return "medium";
        }

        return "low";
    }
}

/// <summary>
/// The form fields that the OCR engine can fill in.
/// </summary>
/// <remarks>
/// The constant names are the OCR field names the API returns
/// (see <c>docs/OCR-Cloudinary-Spec.md</c> §3.3) - they are NOT the form field
/// names. "issueDate" is the OCR name; "IssuedDate" is the form field.
/// </remarks>
public static class OcrFieldNames
{
    public const string DocumentNumber = "documentNumber";
    public const string IssueDate = "issueDate";
    public const string ExpiryDate = "expiryDate";

    /// <summary>Display names used in the result panel.</summary>
    public static readonly IReadOnlyDictionary<string, string> DisplayNames = new Dictionary<string, string>
    {
        [DocumentNumber] = "Số giấy tờ",
        ["fullName"] = "Họ và tên",
        ["dateOfBirth"] = "Ngày sinh",
        ["gender"] = "Giới tính",
        ["address"] = "Địa chỉ",
        [IssueDate] = "Ngày cấp",
        [ExpiryDate] = "Ngày hết hạn",
        ["authorityNo"] = "Số hiệu"
    };
}
