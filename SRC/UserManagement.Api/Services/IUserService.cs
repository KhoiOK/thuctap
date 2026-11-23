using UserManagement.Api.DTOs.Users;

namespace UserManagement.Api.Services;

/// <summary>
/// Business operations for the User aggregate. Implementations live in <c>UserService</c>
/// and are responsible for normalization, validation, persistence, and DTO projection.
/// </summary>
public interface IUserService
{
    Task<PagedResultDto<UserListItemDto>> GetPagedAsync(UserListQuery query, CancellationToken cancellationToken);

    Task<UserDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<UserDetailDto> CreateAsync(UserUpsertRequest request, CancellationToken cancellationToken);

    Task<UserDetailDto> UpdateAsync(Guid id, UserUpsertRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Soft-delete a user and physically remove their identity documents and image files.
    /// Returns <c>true</c> if the user existed and was active; <c>false</c> otherwise.
    /// </summary>
    Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken);
}
