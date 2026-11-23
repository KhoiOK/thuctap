namespace UserManagement.Api.Ocr;

/// <summary>
/// Configuration for the OCR integration. All values come from the <c>Ocr</c>
/// configuration section.
/// </summary>
public class OcrOptions
{
    public const string SectionName = "Ocr";

    /// <summary>
    /// Master switch. When false, the OCR endpoints return 503 and the frontend keeps
    /// the manual entry flow working.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Base URL of the PaddleOCR service, e.g. <c>http://localhost:8100</c>.</summary>
    public string BaseUrl { get; set; } = "http://localhost:8100";

    /// <summary>Timeout for a single OCR request.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Minimum confidence a field must reach to be auto-filled into the form (BR-14).
    /// </summary>
    public double MinConfidence { get; set; } = 0.60;

    /// <summary>Confidence at or above which a field is shown with the "high confidence" badge.</summary>
    public double HighConfidence { get; set; } = 0.85;

    /// <summary>Maximum size in bytes of a single image sent to the OCR engine.</summary>
    public long MaxScanImageBytes { get; set; } = 10_485_760L;

    /// <summary>
    /// Whether a two-digit year on the identity card may be expanded to a four-digit year
    /// (e.g. <c>20/04/95</c> to <c>1995-04-20</c>). Fields expanded this way are always
    /// marked as "needs review".
    /// </summary>
    public bool AllowTwoDigitYearInference { get; set; } = true;
}
