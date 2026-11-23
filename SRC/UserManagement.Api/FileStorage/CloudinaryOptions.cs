namespace UserManagement.Api.FileStorage;

/// <summary>
/// Configuration for Cloudinary image storage. All values come from the
/// <c>Cloudinary</c> configuration section. The <see cref="ApiSecret"/> must NEVER be
/// committed to source control - use user secrets, environment variables
/// (<c>Cloudinary__ApiSecret</c>) or a secret manager.
/// </summary>
public class CloudinaryOptions
{
    public const string SectionName = "Cloudinary";

    /// <summary>The Cloudinary cloud name (the first segment of every delivery URL).</summary>
    public string CloudName { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>API secret used to sign uploads. Never log or return this value.</summary>
    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>Root folder inside the Cloudinary account, e.g. <c>user-management</c>.</summary>
    public string Folder { get; set; } = "user-management";

    /// <summary>
    /// When true, delivery URLs use the <c>image/authenticated</c> delivery type so the
    /// assets are not publicly listable (BR-17).
    /// </summary>
    public bool UseSecureDelivery { get; set; } = true;

    /// <summary>Maximum size in bytes of a single uploaded image (default 5 MB).</summary>
    public long MaxFileSizeBytes { get; set; } = 5_242_880L;

    /// <summary>
    /// Formats accepted by Cloudinary for this application.
    /// </summary>
    /// <remarks>
    /// The setter de-duplicates the values on purpose. ASP.NET Core's configuration
    /// binder CONCATENATES array values when several configuration sources contribute
    /// the same key (e.g. <c>appsettings.json</c> plus <c>appsettings.Development.json</c>
    /// plus a local secrets file). Without de-duplication <c>AllowedFormats</c> became
    /// "jpg,png,jpg,png", which changed the string-to-sign and made Cloudinary reject
    /// every upload with "Invalid Signature".
    /// </remarks>
    public string[] AllowedFormats
    {
        get => _allowedFormats;
        set => _allowedFormats = (value ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string[] _allowedFormats = ["jpg", "png"];

    /// <summary>
    /// When true, deleting or replacing an asset also invalidates the CDN cache so the
    /// old image stops being served (BR-16).
    /// </summary>
    public bool AutoInvalidate { get; set; } = true;

    /// <summary>
    /// How long a temporary OCR image is kept before it is cleaned up (BR-15).
    /// </summary>
    public TimeSpan TemporaryImageLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Timeout for calls to the Cloudinary API.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
