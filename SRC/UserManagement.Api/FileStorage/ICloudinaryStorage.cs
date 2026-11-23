namespace UserManagement.Api.FileStorage;

/// <summary>
/// Result of a successful Cloudinary upload: the public id (including the folder),
/// the asset version, the byte size and the detected content type.
/// </summary>
public sealed record CloudinaryUploadResult(
    string PublicId,
    long Version,
    string ContentType,
    long Size,
    int Width,
    int Height);

/// <summary>
/// Abstraction over the Cloudinary media API. Implementations must never expose the
/// API secret and must only return public ids (never full delivery URLs) so the URL
/// strategy stays in one place.
/// </summary>
public interface ICloudinaryStorage
{
    /// <summary>Whether the storage is configured and usable.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Uploads an image and returns its public id. <paramref name="folder"/> is relative to
    /// the configured root folder.
    /// </summary>
    Task<CloudinaryUploadResult> UploadAsync(
        string folder,
        string fileNamePrefix,
        Stream source,
        string contentType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves an asset to another folder by copying it and deleting the source.
    /// Used to promote temporary OCR images to their permanent location.
    /// </summary>
    Task<CloudinaryUploadResult> MoveAsync(
        string sourcePublicId,
        string targetFolder,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the given public ids. Missing assets are ignored. Failures are reported
    /// through the returned list rather than thrown (BR-16 - a failed delete must never
    /// roll back the database).
    /// </summary>
    Task<IReadOnlyList<string>> DeleteAsync(IEnumerable<string> publicIds, CancellationToken cancellationToken);

    /// <summary>
    /// Builds the delivery URL for a public id. With
    /// <see cref="CloudinaryOptions.UseSecureDelivery"/> the asset is delivered through
    /// the <c>image/authenticated</c> type and the URL carries a signature computed from
    /// the public id and the API secret - Cloudinary rejects unsigned URLs for those
    /// assets with HTTP 401.
    /// </summary>
    string BuildDeliveryUrl(string publicId);

    /// <summary>
    /// Downloads an asset and returns its bytes together with the content type. Used by
    /// the image proxy (API-10) which streams the image through the API.
    /// </summary>
    Task<CloudinaryDownloadResult?> DownloadAsync(string publicId, CancellationToken cancellationToken);
}

/// <summary>Bytes and metadata of a downloaded Cloudinary asset.</summary>
public sealed record CloudinaryDownloadResult(
    Stream Content,
    string ContentType,
    long Length,
    long Version);
