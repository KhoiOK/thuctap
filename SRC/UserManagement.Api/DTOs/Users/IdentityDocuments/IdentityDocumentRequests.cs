using Microsoft.AspNetCore.Http;
using UserManagement.Api.Entities;

namespace UserManagement.Api.DTOs.Users.IdentityDocuments;

/// <summary>
/// Multipart payload for <c>POST /users/{userId}/identity-documents</c> (API-07).
/// </summary>
public class CreateIdentityDocumentRequest
{
    public DocumentType DocumentType { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public DateOnly? IssuedDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public IFormFile? FrontImage { get; set; }
    public IFormFile? BackImage { get; set; }
}

/// <summary>
/// Multipart payload for <c>PUT /users/{userId}/identity-documents/{documentId}</c> (API-08).
/// DocumentType is intentionally absent (API-Spec §3 says it cannot be changed).
/// </summary>
public class UpdateIdentityDocumentRequest
{
    public string DocumentNumber { get; set; } = string.Empty;
    public DateOnly? IssuedDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public IFormFile? FrontImage { get; set; }
    public IFormFile? BackImage { get; set; }
}
