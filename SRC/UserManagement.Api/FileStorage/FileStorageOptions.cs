namespace UserManagement.Api.FileStorage;

/// <summary>
/// Configuration for the file storage location and upload limits.
/// All values are loaded from configuration (FileStorage section).
/// </summary>
public class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>
    /// Root directory where identity document images are stored.
    /// Must be outside of any wwwroot folder.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// Maximum size in bytes of a single image (default 5 MB).
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 5_242_880L;

    /// <summary>
    /// Maximum total request body size in bytes for multipart uploads (default ~11 MB).
    /// </summary>
    public long MaxRequestBodyBytes { get; set; } = 11_534_336L;

    /// <summary>
    /// Maximum pixel dimension (width or height) of a single image (default 8000).
    /// </summary>
    public int MaxPixelSize { get; set; } = 8000;
}
