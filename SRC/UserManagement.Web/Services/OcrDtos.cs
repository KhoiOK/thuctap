using System.Text.Json.Serialization;

namespace UserManagement.Web.Services;

/// <summary>
/// Response of API-12 (OCR scan). Mirrors the Backend's
/// <c>OcrScanResponse</c>.
/// </summary>
public sealed record OcrScanResponseDto(
    [property: JsonPropertyName("scanId")] Guid ScanId,
    [property: JsonPropertyName("documentType")] string DocumentType,
    [property: JsonPropertyName("fields")] IReadOnlyDictionary<string, OcrFieldDto> Fields,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("processingTimeMs")] int ProcessingTimeMs);

/// <summary>
/// One extracted field. <see cref="IsValid"/> tells the frontend whether the value
/// passed the business validation (BR-14).
/// </summary>
public sealed record OcrFieldDto(
    [property: JsonPropertyName("value")] string? Value,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("isValid")] bool IsValid,
    [property: JsonPropertyName("isInferred")] bool IsInferred)
{
    /// <summary>The field has a usable value.</summary>
    public bool HasValue => !string.IsNullOrWhiteSpace(Value);
}

/// <summary>
/// Payload of API-13 (OCR confirm). Sent when the user saves the reviewed form.
/// </summary>
public sealed record OcrConfirmRequest(
    [property: JsonPropertyName("scanId")] Guid ScanId,
    [property: JsonPropertyName("documentType")] string DocumentType,
    [property: JsonPropertyName("documentNumber")] string DocumentNumber,
    [property: JsonPropertyName("issuedDate")] DateOnly? IssuedDate,
    [property: JsonPropertyName("expiryDate")] DateOnly? ExpiryDate);
