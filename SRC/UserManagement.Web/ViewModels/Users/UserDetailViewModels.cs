using System.ComponentModel.DataAnnotations;
using UserManagement.Web.Services;

namespace UserManagement.Web.ViewModels.Users;

/// <summary>
/// SCR-02: view model for the user details page, including their identity documents.
/// </summary>
public class UserDetailViewModel
{
    public UserDetailDto? User { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ReturnUrl { get; set; }

    /// <summary>User has 3 documents already (one of each type) when this is true.</summary>
    public bool HasAllDocumentTypes => User is not null && User.IdentityDocuments.Count >= 3;

    public string GenderDisplay => User?.Gender switch
    {
        "male" => "Nam",
        "female" => "Nữ",
        "other" => "Khác",
        _ => "---"
    };
}

/// <summary>
/// SCR-06: backing view model for the create/edit identity document form.
/// </summary>
public class IdentityDocumentFormViewModel
{
    public Guid UserId { get; set; }
    public Guid? DocumentId { get; set; }
    public bool IsEdit => DocumentId.HasValue;

    [Required(ErrorMessage = "Loại giấy tờ là bắt buộc.")]
    [Display(Name = "Loại giấy tờ")]
    public string DocumentType { get; set; } = "CCCD";

    [Required(ErrorMessage = "Số giấy tờ là bắt buộc.")]
    [StringLength(50, MinimumLength = 6,
        ErrorMessage = "Số giấy tờ phải có từ 6 đến 50 ký tự.")]
    [Display(Name = "Số giấy tờ")]
    public string DocumentNumber { get; set; } = string.Empty;

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
    /// The id of the OCR scan that produced the pre-filled values. When it is set, the
    /// form is submitted through API-13 (OCR confirm) so the temporary images are
    /// promoted to their permanent location instead of being uploaded a second time.
    /// </summary>
    public Guid? OcrScanId { get; set; }

    /// <summary>URL of the current front image (used as preview in edit mode).</summary>
    public string? ExistingFrontImageUrl { get; set; }

    /// <summary>URL of the current back image (used as preview in edit mode).</summary>
    public string? ExistingBackImageUrl { get; set; }

    public string? ReturnUrl { get; set; }
    public string? ErrorMessage { get; set; }
    public IDictionary<string, string[]> FieldErrors { get; set; } = new Dictionary<string, string[]>();

    /// <summary>State of the automatic scan (SCR-07). Null when no scan was performed.</summary>
    public OcrScanViewModel? Ocr { get; set; }

    /// <summary>Minimum confidence required to auto-fill a field (BR-14).</summary>
    public double OcrMinConfidence { get; set; } = 0.60;

    /// <summary>Confidence at or above which a field is shown as highly reliable.</summary>
    public double OcrHighConfidence { get; set; } = 0.85;

    /// <summary>True when a scan has been performed and produced a result.</summary>
    public bool HasOcrResult => Ocr is { HasResult: true };

    /// <summary>True when the document was created from a scan (shown as a badge).</summary>
    public bool CreatedFromScan { get; set; }

    public string Title => IsEdit ? "Chỉnh sửa giấy tờ" : "Thêm giấy tờ";
    public string SubmitText => IsEdit ? "Lưu thay đổi" : "Thêm";

    /// <summary>Whether the form should expose the back image upload field (CCCD/CMND only).</summary>
    public bool ShowsBackImageInput => !IsEdit || DocumentType.Equals("CCCD", StringComparison.OrdinalIgnoreCase) || DocumentType.Equals("CMND", StringComparison.OrdinalIgnoreCase);
}
