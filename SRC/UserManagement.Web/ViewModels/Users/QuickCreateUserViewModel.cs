using System.ComponentModel.DataAnnotations;
using UserManagement.Web.Services;

namespace UserManagement.Web.ViewModels.Users;

/// <summary>
/// Backing view model for the "Quick Create" flow (Thêm User nhanh).
///
/// The form is intentionally short: only the fields that the current backend rules
/// mark as required (BR-01..BR-03) plus a couple of optional ones. Everything else
/// can be filled in later on the user details page.
///
/// Two input modes are supported:
/// - <see cref="QuickCreateMode.Manual"/>: the user types the data.
/// - <see cref="QuickCreateMode.Ocr"/>: the user uploads CCCD images, the OCR preview
///   (API-12b) pre-fills the form and the user reviews the values before saving.
/// </summary>
public class QuickCreateUserViewModel
{
    /// <summary>The two ways of feeding data into the quick-create form.</summary>
    public const string ModeManual = "manual";
    public const string ModeOcr = "ocr";

    /// <summary>
    /// The active input mode: <c>manual</c> or <c>ocr</c>.
    /// </summary>
    public string Mode { get; set; } = ModeManual;

    /// <summary>True when the OCR branch is active.</summary>
    public bool IsOcrMode => string.Equals(Mode, ModeOcr, StringComparison.OrdinalIgnoreCase);

    // ---------- User fields (mirror of UserFormViewModel) ----------

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

    // ---------- Identity document (optional) ----------

    [Display(Name = "Loại giấy tờ")]
    public string DocumentType { get; set; } = "CCCD";

    [Display(Name = "Số giấy tờ")]
    public string? DocumentNumber { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Ngày cấp")]
    public DateOnly? IssuedDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Ngày hết hạn")]
    public DateOnly? ExpiryDate { get; set; }

    [Display(Name = "Ảnh mặt trước")]
    public IFormFile? FrontImage { get; set; }

    [Display(Name = "Ảnh mặt sau")]
    public IFormFile? BackImage { get; set; }

    /// <summary>
    /// True when the user asked for an identity document to be created together with the
    /// user (either via OCR or by uploading the images).
    /// </summary>
    public bool CreateDocument => IsOcrMode;

    // ---------- OCR state ----------

    /// <summary>The result of the last OCR preview (null when no preview was run).</summary>
    public OcrScanViewModel? Ocr { get; set; }

    /// <summary>Minimum confidence required to auto-fill a field (BR-14).</summary>
    public double OcrMinConfidence { get; set; } = 0.60;

    /// <summary>Confidence at or above which a field is shown as highly reliable.</summary>
    public double OcrHighConfidence { get; set; } = 0.85;

    /// <summary>True when a preview produced a result the user can review.</summary>
    public bool HasOcrResult => Ocr is { HasResult: true };

    /// <summary>
    /// Set when the user record was created but the identity document could not be
    /// saved. The user must NOT be created again - the flow offers to continue with the
    /// document only (business rule 7).
    /// </summary>
    public Guid? CreatedUserId { get; set; }

    /// <summary>Name of the already created user (for the partial-success message).</summary>
    public string? CreatedUserName { get; set; }

    /// <summary>True when the user was created but the document step failed.</summary>
    public bool IsDocumentPending => CreatedUserId.HasValue;

    // ---------- Form state ----------

    /// <summary>Optional return URL so we can go back to the list state.</summary>
    public string? ReturnUrl { get; set; }

    /// <summary>API-supplied field errors keyed by camelCase API field names.</summary>
    public IDictionary<string, string[]> FieldErrors { get; set; } = new Dictionary<string, string[]>();

    /// <summary>A general (non field-specific) error message.</summary>
    public string? GeneralError { get; set; }

    public string Title => "Thêm User nhanh";
    public string SubmitText => "Tạo User";

    /// <summary>True when the back image is required for the selected document type.</summary>
    public bool RequiresBackImage => DocumentType.Equals("CCCD", StringComparison.OrdinalIgnoreCase)
        || DocumentType.Equals("CMND", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>Returns the first API error for the given field (or null).</summary>
    public string? GetFieldError(string apiField)
    {
        if (FieldErrors.TryGetValue(apiField, out var errs) && errs.Length > 0) return errs[0];
        return null;
    }

    /// <summary>
    /// Validates only the USER part of the form.
    ///
    /// The identity-document rules are deliberately NOT validated here: the document is
    /// a separate step that runs AFTER the user has been created (business rule 7), so a
    /// bad document number must never prevent the user from being created. The
    /// document rules are checked separately by <see cref="ValidateDocument"/> and the
    /// API stays the final authority (BR-08, BR-10).
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        yield break;
    }

    /// <summary>
    /// Validates the identity-document part of the form.
    ///
    /// Returns a dictionary of field name -> error messages. An empty dictionary means
    /// the document data is acceptable for the document step.
    /// </summary>
    public IDictionary<string, string[]> ValidateDocument()
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!CreateDocument)
        {
            return errors;
        }

        var number = DocumentNumber?.Trim() ?? string.Empty;

        if (number.Length == 0)
        {
            errors[nameof(DocumentNumber)] = new[] { "Số giấy tờ là bắt buộc khi tạo kèm giấy tờ." };
        }
        else if (number.Length is < 6 or > 50)
        {
            errors[nameof(DocumentNumber)] = new[] { "Số giấy tờ phải có từ 6 đến 50 ký tự." };
        }

        if (IssuedDate is { } issued && ExpiryDate is { } expiry && expiry < issued)
        {
            errors[nameof(ExpiryDate)] = new[] { "Ngày hết hạn không được trước ngày cấp." };
        }

        return errors;
    }
}
