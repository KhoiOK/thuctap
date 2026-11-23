using UserManagement.Api.Entities;

namespace UserManagement.Api.Ocr;

/// <summary>
/// A single field extracted from an identity document image.
/// </summary>
/// <param name="Value">The normalized value, or <c>null</c> when nothing could be read.</param>
/// <param name="Confidence">Engine confidence between 0 and 1.</param>
/// <param name="Source">Which side of the document the value came from.</param>
/// <param name="IsValid">
/// Whether the value passes the business validation rules. Invalid values are still
/// returned so the UI can show the matching error (BR-14).
/// </param>
/// <param name="IsInferred">Whether the value was derived (e.g. a two-digit year was expanded).</param>
public sealed record OcrField(
    string? Value,
    double Confidence,
    OcrFieldSource Source,
    bool IsValid,
    bool IsInferred = false)
{
    /// <summary>Convenience flag for JSON serialization.</summary>
    public bool HasValue => !string.IsNullOrWhiteSpace(Value);
}

/// <summary>Which side of the identity document a field was read from.</summary>
public enum OcrFieldSource
{
    Front = 0,
    Back = 1
}

/// <summary>Outcome of a scan request.</summary>
public sealed record OcrScanResult(
    Guid ScanId,
    DocumentType DocumentType,
    IReadOnlyDictionary<string, OcrField> Fields,
    IReadOnlyList<string> Warnings,
    int ProcessingTimeMs,
    bool Success,
    string? ErrorMessage)
{
    public static OcrScanResult Failed(Guid scanId, DocumentType documentType, string error, int processingTimeMs) =>
        new(scanId, documentType, new Dictionary<string, OcrField>(), Array.Empty<string>(), processingTimeMs, false, error);
}
