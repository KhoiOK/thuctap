using Microsoft.AspNetCore.Http;
using UserManagement.Api.Entities;

namespace UserManagement.Api.DTOs.Users.IdentityDocuments;

/// <summary>
/// Multipart payload for <c>POST /identity-documents/ocr/preview</c> (API-12b).
/// Unlike API-12 this payload is NOT bound to a user: it is used by the Quick Create
/// flow to pre-fill the user form before the user record exists.
/// </summary>
public class OcrPreviewRequest
{
    public DocumentType DocumentType { get; set; } = DocumentType.CCCD;

    /// <summary>Image of the front / information page. Always required.</summary>
    public IFormFile? FrontImage { get; set; }

    /// <summary>Image of the back page. Required for CCCD and CMND, not allowed for Passport.</summary>
    public IFormFile? BackImage { get; set; }
}
