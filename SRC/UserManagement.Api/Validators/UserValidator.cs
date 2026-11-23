using UserManagement.Api.DTOs.Users;
using UserManagement.Api.Exceptions;

namespace UserManagement.Api.Validators;

/// <summary>
/// Runs business-rule checks (BR-01..BR-04) that DataAnnotations can't express because they
/// require normalization. Throws <see cref="ValidationException"/> on failure; field names
/// use camelCase to match the API contract.
/// </summary>
public static class UserValidator
{
    public static (string FullName, string Email, string PhoneNumber) ValidateAndNormalize(UserUpsertRequest req)
    {
        var errors = new Dictionary<string, List<string>>();

        // FullName (BR-03): trim + 2..100.
        var fullName = (req.FullName ?? string.Empty).Trim();
        if (fullName.Length < 2 || fullName.Length > 100)
        {
            AddError(errors, "fullName", "Họ và tên phải có từ 2 đến 100 ký tự sau khi loại bỏ khoảng trắng thừa.");
        }

        // Email (BR-01): trim + lowercase + valid format + length.
        var email = NormalizationHelpers.NormalizeEmail(req.Email);
        if (!NormalizationHelpers.IsValidEmail(email))
        {
            AddError(errors, "email", "Email không hợp lệ.");
        }

        // PhoneNumber (BR-02): normalize then validate.
        var phone = NormalizationHelpers.NormalizePhoneNumber(req.PhoneNumber);
        if (!NormalizationHelpers.IsValidPhoneNumber(phone))
        {
            AddError(errors, "phoneNumber", "Số điện thoại không hợp lệ. Ví dụ hợp lệ: 0912345678.");
        }

        // DateOfBirth (BR-04): optional, in [1900-01-01, today].
        if (!NormalizationHelpers.IsValidDateOfBirth(req.DateOfBirth, DateOnly.FromDateTime(DateTime.UtcNow)))
        {
            AddError(errors, "dateOfBirth", "Ngày sinh phải từ 01/01/1900 đến hôm nay.");
        }

        // Address length (already checked by DataAnnotation, but cap to be safe).
        if (req.Address is { Length: > 500 })
        {
            AddError(errors, "address", "Địa chỉ tối đa 500 ký tự.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));
        }

        return (fullName, email, phone);
    }

    private static void AddError(Dictionary<string, List<string>> errors, string field, string message)
    {
        if (!errors.TryGetValue(field, out var list))
        {
            list = new List<string>();
            errors[field] = list;
        }
        list.Add(message);
    }
}
