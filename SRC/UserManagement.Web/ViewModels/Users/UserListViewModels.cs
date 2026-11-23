using System.ComponentModel.DataAnnotations;
using UserManagement.Web.Services;

namespace UserManagement.Web.ViewModels.Users;

/// <summary>
/// SCR-01 view model: drives the user list page including filter and paging state.
/// </summary>
public class UserListViewModel
{
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string SortBy { get; set; } = "createdAt";
    public string SortOrder { get; set; } = "desc";

    public PagedResultDto<UserListItemDto>? Result { get; set; }
    public string? ErrorMessage { get; set; }

    // Helpers used by the view to render pagination.

    /// <summary>Page numbers to display around the current page.</summary>
    public IReadOnlyList<int> PageNumbers
    {
        get
        {
            if (Result is null) return Array.Empty<int>();
            var total = Result.TotalPages;
            if (total <= 7)
            {
                return Enumerable.Range(1, total).ToList();
            }
            var start = Math.Max(1, Page - 2);
            var end = Math.Min(total, Page + 2);
            // Always show 1 and last; collapse neighbours.
            if (start > 2) start = Math.Max(start, 1);
            if (end < total - 1) end = Math.Min(end, total);
            return Enumerable.Range(start, end - start + 1).ToList();
        }
    }

    public bool HasPrev => Result is not null && Page > 1;
    public bool HasNext => Result is not null && Page < Result.TotalPages;

    public string BuildQueryString(int newPage, int? newPageSize = null, string? newSortBy = null, string? newSortOrder = null)
    {
        var qs = new List<string>
        {
            $"page={newPage}",
            $"pageSize={newPageSize ?? PageSize}",
            $"sortBy={Uri.EscapeDataString(newSortBy ?? SortBy)}",
            $"sortOrder={Uri.EscapeDataString(newSortOrder ?? SortOrder)}"
        };
        if (!string.IsNullOrWhiteSpace(Keyword))
        {
            qs.Add($"keyword={Uri.EscapeDataString(Keyword)}");
        }
        return "?" + string.Join("&", qs);
    }

    public string? PreviousSortOrder(string column) => SortBy == column && SortOrder == "asc" ? "desc" : "asc";

    public string SortIndicator(string column) =>
        SortBy == column ? (SortOrder == "asc" ? "▲" : "▼") : string.Empty;
}

/// <summary>
/// SCR-03/04: backing view model for the create/edit form.
/// </summary>
public class UserFormViewModel
{
    [Required(ErrorMessage = "Họ và tên là bắt buộc.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Họ và tên phải từ 2 đến 100 ký tự.")]
    [Display(Name = "Họ và tên")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [StringLength(255, ErrorMessage = "Email tối đa 255 ký tự.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại là bắt buộc.")]
    [RegularExpression(@"^0[35789][0-9]{8}$", ErrorMessage = "Số điện thoại phải đúng định dạng Việt Nam (10 số, đầu 03/05/07/08/09).")]
    [Display(Name = "Số điện thoại")]
    public string PhoneNumber { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "Ngày sinh")]
    public DateOnly? DateOfBirth { get; set; }

    [Display(Name = "Giới tính")]
    public string? Gender { get; set; }

    [StringLength(500, ErrorMessage = "Địa chỉ tối đa 500 ký tự.")]
    [Display(Name = "Địa chỉ")]
    public string? Address { get; set; }

    /// <summary>User being edited (null for Create).</summary>
    public Guid? Id { get; set; }

    /// <summary>When true the form is in edit mode (looser required rules and the ID is read-only).</summary>
    public bool IsEdit => Id.HasValue;

    /// <summary>Optional return URL passed via the query string so we can return to the list state.</summary>
    public string? ReturnUrl { get; set; }

    /// <summary>API-supplied field errors keyed by camelCase API field names.</summary>
    public IDictionary<string, string[]> FieldErrors { get; set; } = new Dictionary<string, string[]>();

    public string? GeneralError { get; set; }

    public string Title => IsEdit ? "Chỉnh sửa người dùng" : "Thêm người dùng";
    public string SubmitText => IsEdit ? "Lưu thay đổi" : "Thêm";

    /// <summary>Maps the camelCase API field name to a user-friendly field name.</summary>
    public static readonly IReadOnlyDictionary<string, string> ApiFieldToForm = new Dictionary<string, string>
    {
        ["fullName"] = nameof(FullName),
        ["email"] = nameof(Email),
        ["phoneNumber"] = nameof(PhoneNumber),
        ["dateOfBirth"] = nameof(DateOfBirth),
        ["gender"] = nameof(Gender),
        ["address"] = nameof(Address)
    };

    public string? GetFieldError(string apiField)
    {
        if (FieldErrors.TryGetValue(apiField, out var errs) && errs.Length > 0) return errs[0];
        return null;
    }
}
