using UserManagement.Api.Entities;
using UserManagement.Api.Ocr;

namespace UserManagement.Api.DTOs.Users.IdentityDocuments;

/// <summary>
/// Response of API-12. Contains the normalized fields with their confidence so the
/// frontend can auto-fill the form and show the confidence badges.
/// </summary>
public sealed record OcrScanResponse(
    Guid ScanId,
    DocumentType DocumentType,
    IReadOnlyDictionary<string, OcrField> Fields,
    IReadOnlyList<string> Warnings,
    int ProcessingTimeMs);

/// <summary>
/// Response of API-12b (OCR preview). It carries the same normalized fields as API-12
/// but no <see cref="ScanId"/>: a preview is not persisted and cannot be confirmed.
/// It only exists to pre-fill the Quick Create form.
/// </summary>
public sealed record OcrPreviewResponse(
    DocumentType DocumentType,
    IReadOnlyDictionary<string, OcrField> Fields,
    IReadOnlyList<string> Warnings,
    int ProcessingTimeMs);

/// <summary>
/// One field in the API-12 response. The shape is intentionally flat so the frontend
/// can render it without extra mapping.
/// </summary>
public sealed record OcrFieldDto(
    string? Value,
    double Confidence,
    string Source,
    bool IsValid,
    bool IsInferred);
