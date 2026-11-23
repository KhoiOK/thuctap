using Microsoft.AspNetCore.Http;
using UserManagement.Api.Entities;

namespace UserManagement.Api.DTOs.Users.IdentityDocuments;

/// <summary>
/// Multipart payload for <c>POST /users/{userId}/identity-documents/ocr/scan</c> (API-12).
/// </summary>
public class OcrScanRequest
{
    public DocumentType DocumentType { get; set; } = DocumentType.CCCD;

    /// <summary>Image of the front / information page. Always required.</summary>
    public IFormFile? FrontImage { get; set; }

    /// <summary>Image of the back page. Required for CCCD and CMND, not allowed for Passport.</summary>
    public IFormFile? BackImage { get; set; }
}

/// <summary>
/// JSON payload for <c>POST /users/{userId}/identity-documents/ocr/confirm</c> (API-13).
/// The values are the ones the user reviewed in the form.
/// </summary>
public class OcrConfirmRequest
{
    /// <summary>Identifies the scan that produced the images.</summary>
    public Guid ScanId { get; set; }

    public DocumentType? DocumentType { get; set; }

    public string DocumentNumber { get; set; } = string.Empty;

    public DateOnly? IssuedDate { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    /// <summary>
    /// Temporary public id of the front image. Falls back to the id stored with the scan
    /// when omitted.
    /// </summary>
    public string? FrontImagePublicId { get; set; }

    /// <summary>
    /// Temporary public id of the back image. Falls back to the id stored with the scan
    /// when omitted.
    /// </summary>
    public string? BackImagePublicId { get; set; }
}
