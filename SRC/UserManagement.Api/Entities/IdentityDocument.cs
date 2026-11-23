namespace UserManagement.Api.Entities;

/// <summary>
/// Identity document attached to a <see cref="User"/>. Holds metadata only - the actual
/// image bytes live on Cloudinary. The <see cref="FrontImagePublicId"/> and
/// <see cref="BackImagePublicId"/> values are the Cloudinary public ids (including the
/// folder path) so that the absolute CDN URL never leaks into API responses.
/// </summary>
public class IdentityDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public DocumentType DocumentType { get; set; }

    /// <summary>Normalized document number (digits only for CCCD/CMND, upper-case for Passport).</summary>
    public string DocumentNumber { get; set; } = string.Empty;

    public DateOnly? IssuedDate { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    /// <summary>Cloudinary public id of the front/information-page image (always set).</summary>
    public string FrontImagePublicId { get; set; } = string.Empty;

    /// <summary>Cloudinary public id of the back image. Null for Passport (BR-07).</summary>
    public string? BackImagePublicId { get; set; }

    /// <summary>Cloudinary version of the front image - used to build the ETag.</summary>
    public long? FrontImageVersion { get; set; }

    /// <summary>Cloudinary version of the back image - used to build the ETag.</summary>
    public long? BackImageVersion { get; set; }

    /// <summary>Pixel width of the front image (display metadata).</summary>
    public int? FrontImageWidth { get; set; }

    /// <summary>Pixel height of the front image (display metadata).</summary>
    public int? FrontImageHeight { get; set; }

    /// <summary>Pixel width of the back image (display metadata).</summary>
    public int? BackImageWidth { get; set; }

    /// <summary>Pixel height of the back image (display metadata).</summary>
    public int? BackImageHeight { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public User? User { get; set; }
}
