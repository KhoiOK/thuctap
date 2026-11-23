using System.ComponentModel.DataAnnotations;
using UserManagement.Api.Entities;

namespace UserManagement.Api.DTOs.Users;

/// <summary>
/// Payload for <c>POST /api/v1/users</c> and <c>PUT /api/v1/users/{id}</c>. All business rules
/// (BR-01..BR-04) are validated in the service layer because they require normalization
/// (lowercase email, BR-02 phone, etc.). <see cref="ValidationContext"/> only enforces the
/// mechanical limits (length, range).
/// </summary>
public sealed class UserUpsertRequest
{
    [Required(AllowEmptyStrings = false, ErrorMessage = "Họ và tên là bắt buộc.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Họ và tên phải có từ 2 đến 100 ký tự.")]
    public string FullName { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "Email là bắt buộc.")]
    [StringLength(255, ErrorMessage = "Email tối đa 255 ký tự.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    public string Email { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "Số điện thoại là bắt buộc.")]
    [StringLength(20, ErrorMessage = "Số điện thoại tối đa 20 ký tự.")]
    public string PhoneNumber { get; set; } = string.Empty;

    public DateOnly? DateOfBirth { get; set; }

    public Gender? Gender { get; set; }

    [StringLength(500, ErrorMessage = "Địa chỉ tối đa 500 ký tự.")]
    public string? Address { get; set; }
}
