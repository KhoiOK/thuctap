namespace UserManagement.Api.Entities;

/// <summary>
/// Status of an OCR scan attempt. Stored as a number in the database
/// (1=Completed, 2=Failed, 3=Confirmed, 4=Expired, 5=Discarded).
/// </summary>
public enum OcrScanStatus
{
    /// <summary>The scan completed and produced (at least partially) usable fields.</summary>
    Completed = 1,

    /// <summary>The scan failed - the OCR engine could not read the images.</summary>
    Failed = 2,

    /// <summary>The user confirmed the scan and an identity document was created from it.</summary>
    Confirmed = 3,

    /// <summary>The temporary images expired without being confirmed (BR-15).</summary>
    Expired = 4,

    /// <summary>The user explicitly discarded the scan.</summary>
    Discarded = 5
}
