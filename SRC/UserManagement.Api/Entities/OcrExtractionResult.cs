namespace UserManagement.Api.Entities;

/// <summary>
/// Audit record of a single OCR scan attempt on an identity document (BR-13, BR-15).
/// Stores the temporary Cloudinary public ids, the raw engine output and the normalized
/// fields so scans can be traced and so the quality of the extraction can be improved
/// later. Temporary images referenced here are deleted 24 hours after the scan.
/// </summary>
public class OcrExtractionResult
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>Public identifier of this scan, returned to the client by API-12.</summary>
    public Guid ScanId { get; set; } = Guid.NewGuid();

    public DocumentType DocumentType { get; set; }

    public OcrScanStatus Status { get; set; }

    /// <summary>Temporary Cloudinary public id of the front image (null if upload failed).</summary>
    public string? FrontImagePublicId { get; set; }

    /// <summary>Temporary Cloudinary public id of the back image (null if upload failed).</summary>
    public string? BackImagePublicId { get; set; }

    /// <summary>The identity document created from this scan, once confirmed.</summary>
    public Guid? DocumentId { get; set; }

    /// <summary>Raw JSON returned by the OCR engine.</summary>
    public string? RawResult { get; set; }

    /// <summary>JSON of the normalized fields (the values shown in the auto-fill form).</summary>
    public string? ExtractedFields { get; set; }

    public int? ProcessingTimeMs { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the temporary images expire and must be cleaned up (BR-15). Defaults to 24 hours
    /// after the scan.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    // Navigation
    public User? User { get; set; }

    public IdentityDocument? Document { get; set; }
}
