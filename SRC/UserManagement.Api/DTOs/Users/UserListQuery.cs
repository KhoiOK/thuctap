namespace UserManagement.Api.DTOs.Users;

/// <summary>
/// Pagination metadata for list endpoints. Field names are camelCase to match API-Spec §1.2.
/// </summary>
public sealed record PagedResultDto<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

/// <summary>
/// Query parameters accepted by <c>GET /api/v1/users</c>. Defaults match API-Spec §3.1.
/// </summary>
public sealed class UserListQuery
{
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string SortBy { get; set; } = "createdAt";
    public string SortOrder { get; set; } = "desc";
}
