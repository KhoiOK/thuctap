using SixLabors.ImageSharp;

namespace UserManagement.Api.FileStorage;

/// <summary>
/// Detected image metadata: format, dimensions, and whether the stream looked like a real
/// image (magic bytes match). All values are derived from the byte stream alone, never
/// from the file extension or Content-Type header.
/// </summary>
public sealed record DetectedImage(string Format, int Width, int Height, bool StripExif);

/// <summary>
/// Result of validating an uploaded image. If <see cref="IsValid"/> is <c>false</c>,
/// <see cref="Error"/> contains a user-friendly message and <see cref="StatusCode"/>
/// is the appropriate HTTP status (400 / 415).
/// </summary>
public sealed class ImageValidationResult
{
    public bool IsValid { get; }
    public string? ContentType { get; }
    public DetectedImage? Image { get; }
    public string? Error { get; }
    public int StatusCode { get; }

    private ImageValidationResult(bool isValid, string? contentType, DetectedImage? image, string? error, int statusCode)
    {
        IsValid = isValid;
        ContentType = contentType;
        Image = image;
        Error = error;
        StatusCode = statusCode;
    }

    public static ImageValidationResult Success(string contentType, DetectedImage image)
        => new(true, contentType, image, null, 200);

    public static ImageValidationResult Invalid(string error, int statusCode = 400)
        => new(false, null, null, error, statusCode);
}

/// <summary>
/// Image validation pipeline. Uses ImageSharp to identify the format from magic bytes,
/// measure dimensions, and (best-effort) strip EXIF on re-save. The output is a
/// <see cref="DetectedImage"/> so callers can build a server-side whitelist without
/// trusting client-supplied metadata.
/// </summary>
public class ImageValidator
{
    private readonly long _maxFileSizeBytes;
    private readonly int _maxPixelSize;
    private readonly ILogger<ImageValidator> _logger;

    public ImageValidator(
        Microsoft.Extensions.Options.IOptions<FileStorageOptions> options,
        ILogger<ImageValidator> logger)
    {
        _maxFileSizeBytes = options.Value.MaxFileSizeBytes;
        _maxPixelSize = options.Value.MaxPixelSize > 0 ? options.Value.MaxPixelSize : 8000;
        _logger = logger;
    }

    /// <summary>
    /// Validates an uploaded image by:
    /// 1. Checking the file size against the configured limit.
    /// 2. Rewinding and probing the format via ImageSharp's magic-byte detection.
    /// 3. Loading the image (decode) so we can read dimensions and reject corrupt data.
    /// 4. Verifying dimensions do not exceed the configured max pixel size.
    /// </summary>
    /// <param name="stream">The uploaded stream. Position is rewound before returning.</param>
    /// <param name="declaredContentType">Client-supplied Content-Type (used for hint only, never trusted).</param>
    public async Task<ImageValidationResult> ValidateAsync(Stream stream, string? declaredContentType, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        // Size check first - cheaper than loading the image.
        if (stream.CanSeek && stream.Length > _maxFileSizeBytes)
        {
            return ImageValidationResult.Invalid(
                $"Ảnh vượt quá dung lượng tối đa ({_maxFileSizeBytes:N0} bytes).",
                413);
        }

        // Read into a MemoryStream so we can probe the magic bytes and then load the image.
        // The original stream is then returned to position 0 for the caller.
        byte[] bytes;
        if (stream is MemoryStream ms)
        {
            bytes = ms.ToArray();
        }
        else
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        if (bytes.LongLength > _maxFileSizeBytes)
        {
            return ImageValidationResult.Invalid(
                $"Ảnh vượt quá dung lượng tối đa ({_maxFileSizeBytes:N0} bytes).",
                413);
        }

        if (bytes.Length < 8)
        {
            return ImageValidationResult.Invalid("File ảnh không hợp lệ hoặc bị hỏng.", 415);
        }

        try
        {
            using var image = Image.Load(bytes);

            if (image.Width > _maxPixelSize || image.Height > _maxPixelSize)
            {
                return ImageValidationResult.Invalid(
                    $"Kích thước ảnh vượt quá giới hạn ({_maxPixelSize}x{_maxPixelSize} pixel).",
                    400);
            }

            var (contentType, format) = DetectFormat(bytes, image);
            if (contentType is null)
            {
                return ImageValidationResult.Invalid(
                    "Định dạng ảnh không được hỗ trợ. Chỉ chấp nhận JPG hoặc PNG.",
                    415);
            }

            return ImageValidationResult.Success(contentType,
                new DetectedImage(format, image.Width, image.Height, StripExif: true));
        }
        catch (UnknownImageFormatException)
        {
            return ImageValidationResult.Invalid(
                "File không phải định dạng ảnh hợp lệ. Vui lòng tải lên JPG hoặc PNG.",
                415);
        }
        catch (InvalidImageContentException ex)
        {
            _logger.LogWarning(ex, "Invalid image content during validation.");
            return ImageValidationResult.Invalid(
                "File ảnh bị hỏng hoặc có nội dung không hợp lệ.",
                415);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected error validating image.");
            return ImageValidationResult.Invalid(
                "Không thể xử lý file ảnh. Vui lòng thử lại.",
                400);
        }
        finally
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }
        }
    }

    private static (string? ContentType, string Format) DetectFormat(byte[] bytes, Image image)
    {
        // ImageSharp 3.x sets DecodedImageFormat to the matching IImageFormat. We accept
        // any format whose Name is Jpeg/JPEG or Png/PNG and map it to the canonical
        // MIME type. The mime type is derived from the image (never the client header).
        var name = image.Metadata.DecodedImageFormat?.Name;
        if (name is null)
        {
            // Fall back to magic-byte probing for the common cases. This protects against
            // scenarios where the decoder sets a null DecodedImageFormat (e.g. when the
            // format is registered but missing metadata).
            return ProbeByMagicBytes(bytes);
        }

        return name.ToUpperInvariant() switch
        {
            "JPEG" => ("image/jpeg", "jpg"),
            "PNG" => ("image/png", "png"),
            _ => (null, string.Empty)
        };
    }

    private static (string? ContentType, string Format) ProbeByMagicBytes(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ("image/jpeg", "jpg");
        }
        if (bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            return ("image/png", "png");
        }
        return (null, string.Empty);
    }
}
