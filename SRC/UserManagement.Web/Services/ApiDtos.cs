using System.Text.Json.Serialization;

namespace UserManagement.Web.Services;

/// <summary>
/// API response DTOs that mirror the Backend's DTOs. Kept as separate types in the
/// Web project so we don't take a dependency on the Api project.
/// </summary>
public sealed record PagedResultDto<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record UserListItemDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("phoneNumber")] string PhoneNumber,
    [property: JsonPropertyName("gender")] string? Gender,
    [property: JsonPropertyName("identityDocumentCount")] int IdentityDocumentCount,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt);

public sealed record UserDetailDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("phoneNumber")] string PhoneNumber,
    [property: JsonPropertyName("dateOfBirth")] DateOnly? DateOfBirth,
    [property: JsonPropertyName("gender")] string? Gender,
    [property: JsonPropertyName("address")] string? Address,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTime? UpdatedAt,
    [property: JsonPropertyName("identityDocuments")] IReadOnlyList<IdentityDocumentListItemDto> IdentityDocuments);

public sealed record IdentityDocumentListItemDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("documentType")] string DocumentType,
    [property: JsonPropertyName("documentNumber")] string DocumentNumber,
    [property: JsonPropertyName("issuedDate")] DateOnly? IssuedDate,
    [property: JsonPropertyName("expiryDate")] DateOnly? ExpiryDate,
    [property: JsonPropertyName("hasBackImage")] bool HasBackImage,
    [property: JsonPropertyName("frontImageUrl")] string? FrontImageUrl,
    [property: JsonPropertyName("backImageUrl")] string? BackImageUrl,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTime? UpdatedAt);

public sealed record UserUpsertRequest(
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("phoneNumber")] string PhoneNumber,
    [property: JsonPropertyName("dateOfBirth")] DateOnly? DateOfBirth,
    [property: JsonPropertyName("gender")] string? Gender,
    [property: JsonPropertyName("address")] string? Address);

/// <summary>
/// Parsed RFC 7807 Problem Details. <see cref="Errors"/> holds field-level validation
/// errors keyed by the API's camelCase field name (e.g. "email", "phoneNumber").
/// </summary>
public sealed record ProblemDetailsDto(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("status")] int? Status,
    [property: JsonPropertyName("detail")] string? Detail,
    [property: JsonPropertyName("instance")] string? Instance,
    [property: JsonPropertyName("traceId")] string? TraceId,
    [property: JsonPropertyName("errors")] IDictionary<string, string[]>? Errors)
{
    /// <summary>
    /// Best-effort user-facing message. Falls back to a generic copy based on status code.
    /// </summary>
    public string GetDisplayMessage() => Detail ?? Title ?? GetDefaultForStatus();

    private string GetDefaultForStatus() => Status switch
    {
        400 => "Dữ liệu chưa hợp lệ.",
        404 => "Không tìm thấy dữ liệu.",
        409 => "Dữ liệu bị xung đột.",
        413 => "Tệp tải lên vượt quá dung lượng cho phép.",
        415 => "Định dạng tệp không được hỗ trợ.",
        500 => "Đã xảy ra lỗi máy chủ.",
        _ => "Đã xảy ra lỗi. Vui lòng thử lại."
    };
}
