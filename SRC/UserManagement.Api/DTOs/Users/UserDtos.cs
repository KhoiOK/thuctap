using UserManagement.Api.Entities;

namespace UserManagement.Api.DTOs.Users;

/// <summary>
/// Compact user representation used in list views (API-01 response items).
/// </summary>
public sealed record UserListItemDto(
    Guid Id,
    string FullName,
    string Email,
    string PhoneNumber,
    Gender? Gender,
    int IdentityDocumentCount,
    DateTime CreatedAt);

/// <summary>
/// Detailed user representation used in detail/create/update responses (API-02/03/04).
/// The <see cref="IdentityDocuments"/> collection is empty for create responses and
/// always non-null otherwise.
/// </summary>
public sealed record UserDetailDto(
    Guid Id,
    string FullName,
    string Email,
    string PhoneNumber,
    DateOnly? DateOfBirth,
    Gender? Gender,
    string? Address,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<IdentityDocumentListItemDto> IdentityDocuments);

/// <summary>
/// Compact identity document representation. Used in <see cref="UserDetailDto.IdentityDocuments"/>
/// and as the response shape for the document endpoints (API-06/07/08/11).
/// </summary>
public sealed record IdentityDocumentListItemDto(
    Guid Id,
    DocumentType DocumentType,
    string DocumentNumber,
    DateOnly? IssuedDate,
    DateOnly? ExpiryDate,
    bool HasBackImage,
    string? FrontImageUrl,
    string? BackImageUrl,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
