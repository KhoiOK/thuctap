using Microsoft.EntityFrameworkCore;
using UserManagement.Api.Data;
using UserManagement.Api.DTOs.Users;
using UserManagement.Api.DTOs.Users.IdentityDocuments;
using UserManagement.Api.Entities;
using UserManagement.Api.Exceptions;
using UserManagement.Api.FileStorage;
using UserManagement.Api.Validators;

namespace UserManagement.Api.Services;

/// <summary>
/// Business operations for the IdentityDocument aggregate. Handles upload, retrieval, update,
/// and deletion of identity documents and their associated images (stored on Cloudinary).
/// </summary>
public interface IIdentityDocumentService
{
    Task<IReadOnlyList<IdentityDocumentListItemDto>> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task<IdentityDocumentListItemDto> GetByIdAsync(Guid userId, Guid documentId, CancellationToken cancellationToken);

    Task<IdentityDocumentListItemDto> CreateAsync(Guid userId, CreateIdentityDocumentRequest request, CancellationToken cancellationToken);

    Task<IdentityDocumentListItemDto> UpdateAsync(Guid userId, Guid documentId, UpdateIdentityDocumentRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid userId, Guid documentId, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the image for a given document side. Returns <c>null</c> if any precondition
    /// is not met (user missing, document missing, side invalid, asset missing).
    /// </summary>
    Task<ImageStreamResult?> OpenImageAsync(Guid userId, Guid documentId, string side, CancellationToken cancellationToken);
}

/// <summary>
/// Result of opening an image for streaming. Includes ETag/Last-Modified metadata.
/// </summary>
public sealed record ImageStreamResult(
    Stream Stream,
    string ContentType,
    long Size,
    string ETag,
    DateTimeOffset LastModifiedUtc);

/// <summary>
/// Default <see cref="IIdentityDocumentService"/> implementation. Implements the
/// "write file first, then DB, then cleanup" pattern from API-Spec §4, with the images
/// living on Cloudinary instead of the local disk.
/// </summary>
public class IdentityDocumentService : IIdentityDocumentService
{
    private readonly ApplicationDbContext _db;
    private readonly ICloudinaryStorage _cloudinary;
    private readonly ImageValidator _imageValidator;
    private readonly ILogger<IdentityDocumentService> _logger;
    private readonly TimeProvider _time;

    public IdentityDocumentService(
        ApplicationDbContext db,
        ICloudinaryStorage cloudinary,
        ImageValidator imageValidator,
        ILogger<IdentityDocumentService> logger,
        TimeProvider time)
    {
        _db = db;
        _cloudinary = cloudinary;
        _imageValidator = imageValidator;
        _logger = logger;
        _time = time;
    }

    // ---------- API-06 list ----------

    public async Task<IReadOnlyList<IdentityDocumentListItemDto>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        await EnsureUserExistsAsync(userId, cancellationToken);

        var docs = await _db.IdentityDocuments
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .OrderBy(d => d.DocumentType)
            .ToListAsync(cancellationToken);

        return docs.Select(MapToDto).ToList();
    }

    // ---------- API-11 detail ----------

    public async Task<IdentityDocumentListItemDto> GetByIdAsync(Guid userId, Guid documentId, CancellationToken cancellationToken)
    {
        await EnsureUserExistsAsync(userId, cancellationToken);

        var doc = await _db.IdentityDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId, cancellationToken);

        if (doc is null)
        {
            throw new NotFoundException("Giấy tờ không tồn tại hoặc không thuộc người dùng này.");
        }

        return MapToDto(doc);
    }

    // ---------- API-07 create ----------

    public async Task<IdentityDocumentListItemDto> CreateAsync(Guid userId, CreateIdentityDocumentRequest request, CancellationToken cancellationToken)
    {
        await EnsureUserExistsAsync(userId, cancellationToken);

        ValidateMetadata(request.DocumentType, request.DocumentNumber, request.IssuedDate, request.ExpiryDate);
        await EnsureDocumentTypeSlotAvailableAsync(userId, request.DocumentType, excludeDocumentId: null, cancellationToken);
        await EnsureDocumentNumberAvailableAsync(request.DocumentType, request.DocumentNumber, excludeDocumentId: null, cancellationToken);

        var requiresBack = request.DocumentType is DocumentType.CCCD or DocumentType.CMND;

        // First, validate the image content (this also rejects missing-but-required images).
        var (frontValidated, backValidated) = await ValidateImageContentAsync(
            request.FrontImage, request.BackImage, request.DocumentType, cancellationToken);

        var documentId = Guid.NewGuid();
        var createdAt = _time.GetUtcNow().UtcDateTime;
        var folder = $"identity-documents/{userId}/{documentId}";

        // Upload the images now that we have the final documentId.
        string frontPublicId = string.Empty;
        long frontVersion = 0;
        int frontWidth = 0;
        int frontHeight = 0;
        string? backPublicId = null;
        long backVersion = 0;
        int backWidth = 0;
        int backHeight = 0;

        // Every asset uploaded during this call is tracked here. If anything fails
        // before the database commit, all of them are deleted again - otherwise a
        // failure while uploading the back image would leave the front image behind
        // on Cloudinary (an unreferenced, permanently stored copy of a PII document).
        var uploadedPublicIds = new List<string>();

        try
        {
            if (frontValidated is not null)
            {
                await using var frontStream = request.FrontImage!.OpenReadStream();
                var uploaded = await _cloudinary.UploadAsync(
                    folder, "front", frontStream, frontValidated.ContentType!, cancellationToken);
                frontPublicId = uploaded.PublicId;
                frontVersion = uploaded.Version;
                frontWidth = uploaded.Width;
                frontHeight = uploaded.Height;
                uploadedPublicIds.Add(uploaded.PublicId);
            }

            if (backValidated is not null)
            {
                await using var backStream = request.BackImage!.OpenReadStream();
                var uploaded = await _cloudinary.UploadAsync(
                    folder, "back", backStream, backValidated.ContentType!, cancellationToken);
                backPublicId = uploaded.PublicId;
                backVersion = uploaded.Version;
                backWidth = uploaded.Width;
                backHeight = uploaded.Height;
                uploadedPublicIds.Add(uploaded.PublicId);
            }
        }
        catch
        {
            // The upload of the second image failed (or the first one did) - remove
            // whatever was already uploaded so no orphan is left behind.
            await CleanupAssetsAsync(uploadedPublicIds);
            throw;
        }

        var doc = new IdentityDocument
        {
            Id = documentId,
            UserId = userId,
            DocumentType = request.DocumentType,
            DocumentNumber = NormalizationHelpers.NormalizeDocumentNumber(request.DocumentType, request.DocumentNumber),
            IssuedDate = request.IssuedDate,
            ExpiryDate = request.ExpiryDate,
            FrontImagePublicId = frontPublicId,
            BackImagePublicId = backPublicId,
            FrontImageVersion = frontVersion,
            BackImageVersion = backVersion,
            FrontImageWidth = frontWidth > 0 ? frontWidth : null,
            FrontImageHeight = frontHeight > 0 ? frontHeight : null,
            BackImageWidth = backWidth > 0 ? backWidth : null,
            BackImageHeight = backHeight > 0 ? backHeight : null,
            CreatedAt = createdAt
        };

        _db.IdentityDocuments.Add(doc);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Concurrent duplicate insert - clean up the assets we just uploaded and re-throw.
            await CleanupAssetsAsync(uploadedPublicIds);
            throw;
        }

        return MapToDto(doc);
    }

    /// <summary>
    /// Validates the image content for the front and back files. Returns the validation
    /// results (or <c>null</c> if the file was absent and optional). Throws a
    /// <see cref="ValidationException"/> if a required file is missing.
    /// </summary>
    private async Task<(ImageValidationResult? front, ImageValidationResult? back)> ValidateImageContentAsync(
        Microsoft.AspNetCore.Http.IFormFile? frontFile,
        Microsoft.AspNetCore.Http.IFormFile? backFile,
        DocumentType documentType,
        CancellationToken cancellationToken)
    {
        var front = await ValidateOptionalImageAsync(frontFile, "front", expectRequired: true, cancellationToken);
        var back = await ValidateOptionalImageAsync(backFile, "back",
            expectRequired: documentType is DocumentType.CCCD or DocumentType.CMND,
            cancellationToken);

        if (documentType == DocumentType.Passport && back is not null)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["backImage"] = new[] { "Passport không được có ảnh mặt sau." }
            });
        }
        return (front, back);
    }

    private async Task<ImageValidationResult?> ValidateOptionalImageAsync(
        Microsoft.AspNetCore.Http.IFormFile? file,
        string side,
        bool expectRequired,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            if (expectRequired)
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    [$"{side}Image"] = new[] { $"Ảnh mặt {SideName(side)} là bắt buộc." }
                });
            }
            return null;
        }
        return await ValidateImageContentAsync(file, cancellationToken);
    }

    // ---------- API-08 update ----------

    public async Task<IdentityDocumentListItemDto> UpdateAsync(Guid userId, Guid documentId, UpdateIdentityDocumentRequest request, CancellationToken cancellationToken)
    {
        await EnsureUserExistsAsync(userId, cancellationToken);

        var doc = await _db.IdentityDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId, cancellationToken);

        if (doc is null)
        {
            throw new NotFoundException("Giấy tờ không tồn tại hoặc không thuộc người dùng này.");
        }

        ValidateMetadata(doc.DocumentType, request.DocumentNumber, request.IssuedDate, request.ExpiryDate);
        await EnsureDocumentNumberAvailableAsync(doc.DocumentType, request.DocumentNumber, excludeDocumentId: doc.Id, cancellationToken);

        var folder = $"identity-documents/{userId}/{documentId}";
        var newFrontPublicId = doc.FrontImagePublicId;
        var newBackPublicId = doc.BackImagePublicId;
        long newFrontVersion = doc.FrontImageVersion ?? 0;
        long newBackVersion = doc.BackImageVersion ?? 0;
        var assetsToCleanupOnFailure = new List<string>();
        var assetsToDeleteOnSuccess = new List<string>();

        // ---- Validate EVERYTHING before uploading anything ----
        // The Passport rule ("no back image") used to be checked only after the front
        // image had already been uploaded, which left the new front image orphaned when
        // the check failed. Validation must not have side effects, so it all happens here.
        if (request.BackImage is { Length: > 0 } && doc.DocumentType == DocumentType.Passport)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["backImage"] = new[] { "Passport không được có ảnh mặt sau." }
            });
        }

        var frontValidated = request.FrontImage is { Length: > 0 }
            ? await ValidateImageContentAsync(request.FrontImage, cancellationToken)
            : null;

        var backValidated = request.BackImage is { Length: > 0 }
            ? await ValidateImageContentAsync(request.BackImage, cancellationToken)
            : null;

        try
        {
            if (frontValidated is not null)
            {
                await using var src = request.FrontImage!.OpenReadStream();
                var uploaded = await _cloudinary.UploadAsync(
                    folder, "front", src, frontValidated.ContentType!, cancellationToken);
                assetsToCleanupOnFailure.Add(uploaded.PublicId);
                if (!string.IsNullOrWhiteSpace(doc.FrontImagePublicId))
                {
                    assetsToDeleteOnSuccess.Add(doc.FrontImagePublicId);
                }
                newFrontPublicId = uploaded.PublicId;
                newFrontVersion = uploaded.Version;
                doc.FrontImageWidth = uploaded.Width > 0 ? uploaded.Width : doc.FrontImageWidth;
                doc.FrontImageHeight = uploaded.Height > 0 ? uploaded.Height : doc.FrontImageHeight;
            }

            if (backValidated is not null)
            {
                await using var src = request.BackImage!.OpenReadStream();
                var uploaded = await _cloudinary.UploadAsync(
                    folder, "back", src, backValidated.ContentType!, cancellationToken);
                assetsToCleanupOnFailure.Add(uploaded.PublicId);
                if (!string.IsNullOrWhiteSpace(doc.BackImagePublicId))
                {
                    assetsToDeleteOnSuccess.Add(doc.BackImagePublicId);
                }
                newBackPublicId = uploaded.PublicId;
                newBackVersion = uploaded.Version;
                doc.BackImageWidth = uploaded.Width > 0 ? uploaded.Width : doc.BackImageWidth;
                doc.BackImageHeight = uploaded.Height > 0 ? uploaded.Height : doc.BackImageHeight;
            }
        }
        catch
        {
            // The second upload failed - remove the assets uploaded so far so no
            // orphan is left behind on Cloudinary.
            await CleanupAssetsAsync(assetsToCleanupOnFailure);
            throw;
        }

        doc.DocumentNumber = NormalizationHelpers.NormalizeDocumentNumber(doc.DocumentType, request.DocumentNumber);
        doc.IssuedDate = request.IssuedDate;
        doc.ExpiryDate = request.ExpiryDate;
        doc.FrontImagePublicId = newFrontPublicId;
        doc.BackImagePublicId = newBackPublicId;
        doc.FrontImageVersion = newFrontVersion;
        doc.BackImageVersion = newBackVersion;
        doc.UpdatedAt = _time.GetUtcNow().UtcDateTime;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // DB write failed - clean up any new assets we created and keep the old ones.
            await CleanupAssetsAsync(assetsToCleanupOnFailure);
            throw;
        }

        // DB committed - remove the old assets (best-effort).
        await CleanupAssetsAsync(assetsToDeleteOnSuccess);

        return MapToDto(doc);
    }

    // ---------- API-09 delete ----------

    public async Task DeleteAsync(Guid userId, Guid documentId, CancellationToken cancellationToken)
    {
        await EnsureUserExistsAsync(userId, cancellationToken);

        var doc = await _db.IdentityDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId, cancellationToken);

        if (doc is null)
        {
            throw new NotFoundException("Giấy tờ không tồn tại hoặc không thuộc người dùng này.");
        }

        var frontPublicId = doc.FrontImagePublicId;
        var backPublicId = doc.BackImagePublicId;

        _db.IdentityDocuments.Remove(doc);
        await _db.SaveChangesAsync(cancellationToken);

        // Best-effort cleanup after DB commit (API-Spec §4: xóa DB trước, xóa file sau).
        await CleanupAssetsAsync(new[] { frontPublicId, backPublicId }, documentId);
    }

    // ---------- API-10 image ----------

    public async Task<ImageStreamResult?> OpenImageAsync(Guid userId, Guid documentId, string side, CancellationToken cancellationToken)
    {
        if (side is not ("front" or "back"))
        {
            return null;
        }

        await EnsureUserExistsAsync(userId, cancellationToken);

        var doc = await _db.IdentityDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId, cancellationToken);

        if (doc is null)
        {
            return null;
        }

        var publicId = side == "front" ? doc.FrontImagePublicId : doc.BackImagePublicId;
        if (string.IsNullOrWhiteSpace(publicId))
        {
            return null;
        }

        var downloaded = await _cloudinary.DownloadAsync(publicId, cancellationToken);
        if (downloaded is null)
        {
            return null;
        }

        var version = side == "front" ? doc.FrontImageVersion : doc.BackImageVersion;
        var etag = ComputeEtag(publicId, version, downloaded.Length);
        var lastModified = _time.GetUtcNow();

        return new ImageStreamResult(downloaded.Content, downloaded.ContentType, downloaded.Length, etag, lastModified);
    }

    // ---------- helpers ----------

    private async Task EnsureUserExistsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var exists = await _db.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == userId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException("Người dùng không tồn tại hoặc đã bị xóa.");
        }
    }

    private async Task EnsureDocumentTypeSlotAvailableAsync(Guid userId, DocumentType type, Guid? excludeDocumentId, CancellationToken cancellationToken)
    {
        var query = _db.IdentityDocuments.Where(d => d.UserId == userId && d.DocumentType == type);
        if (excludeDocumentId is not null)
        {
            query = query.Where(d => d.Id != excludeDocumentId.Value);
        }

        if (await query.AnyAsync(cancellationToken))
        {
            throw new ConflictException("documentType",
                $"Người dùng đã có giấy tờ loại {type}. Mỗi người dùng chỉ có một giấy tờ cho mỗi loại.");
        }
    }

    private async Task EnsureDocumentNumberAvailableAsync(DocumentType type, string documentNumber, Guid? excludeDocumentId, CancellationToken cancellationToken)
    {
        var normalized = NormalizationHelpers.NormalizeDocumentNumber(type, documentNumber);
        var query = _db.IdentityDocuments.Where(d => d.DocumentType == type && d.DocumentNumber == normalized);
        if (excludeDocumentId is not null)
        {
            query = query.Where(d => d.Id != excludeDocumentId.Value);
        }

        if (await query.AnyAsync(cancellationToken))
        {
            throw new ConflictException("documentNumber",
                "Số giấy tờ đã được đăng ký cho người dùng khác.");
        }
    }

    /// <summary>
    /// Validates the document metadata (BR-08, BR-10).
    /// </summary>
    internal static void ValidateMetadata(DocumentType type, string documentNumber, DateOnly? issuedDate, DateOnly? expiryDate)
    {
        var errors = new Dictionary<string, List<string>>();

        if (string.IsNullOrWhiteSpace(documentNumber))
        {
            errors["documentNumber"] = new List<string> { "Số giấy tờ là bắt buộc." };
        }
        else if (!NormalizationHelpers.IsValidDocumentNumber(type, documentNumber))
        {
            errors["documentNumber"] = new List<string>
            {
                type switch
                {
                    DocumentType.CCCD => "Số CCCD phải đúng 12 chữ số.",
                    DocumentType.CMND => "Số CMND phải có 9 hoặc 12 chữ số.",
                    DocumentType.Passport => "Số Passport phải có 6-9 ký tự chữ cái hoặc số.",
                    _ => "Loại giấy tờ không hợp lệ."
                }
            };
        }

        if (issuedDate is { } issued && issued > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            errors["issuedDate"] = new List<string> { "Ngày cấp không được lớn hơn hôm nay." };
        }

        if (issuedDate is { } i && expiryDate is { } e && e < i)
        {
            errors["expiryDate"] = new List<string> { "Ngày hết hạn phải lớn hơn hoặc bằng ngày cấp." };
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));
        }
    }

    /// <summary>
    /// Validates the raw image content (size, format, dimensions).
    /// </summary>
    private async Task<ImageValidationResult> ValidateImageContentAsync(Microsoft.AspNetCore.Http.IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = new[] { "File ảnh rỗng." }
            });
        }

        await using var stream = file.OpenReadStream();
        var result = await _imageValidator.ValidateAsync(stream, file.ContentType, cancellationToken);
        if (!result.IsValid)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = new[] { result.Error ?? "File ảnh không hợp lệ." }
            });
        }
        return result;
    }

    private async Task CleanupAssetsAsync(IEnumerable<string?> publicIds, Guid? documentId = null)
    {
        foreach (var id in publicIds)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            try
            {
                var failed = await _cloudinary.DeleteAsync(new[] { id! }, CancellationToken.None);
                if (failed.Count > 0)
                {
                    _logger.LogWarning("Cloudinary asset {PublicId} for document {DocumentId} could not be deleted.",
                        id, documentId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up Cloudinary asset {PublicId} for document {DocumentId}.", id, documentId);
            }
        }
    }

    private static IdentityDocumentListItemDto MapToDto(IdentityDocument doc)
    {
        return new IdentityDocumentListItemDto(
            doc.Id,
            doc.DocumentType,
            doc.DocumentNumber,
            doc.IssuedDate,
            doc.ExpiryDate,
            HasBackImage: !string.IsNullOrWhiteSpace(doc.BackImagePublicId),
            FrontImageUrl: $"/api/v1/users/{doc.UserId}/identity-documents/{doc.Id}/images/front",
            BackImageUrl: string.IsNullOrWhiteSpace(doc.BackImagePublicId)
                ? null
                : $"/api/v1/users/{doc.UserId}/identity-documents/{doc.Id}/images/back",
            doc.CreatedAt,
            doc.UpdatedAt);
    }

    private static string SideName(string side) => side == "front" ? "trước" : "sau";

    private static string ComputeEtag(string publicId, long? version, long size)
    {
        // Format: "W/\"<size>-<version>-<hash>\"" (weak validator). Includes the public id
        // and the Cloudinary version so the tag changes whenever the asset is replaced.
        var versionText = version?.ToString() ?? "0";
        var combined = $"{size}:{versionText}:{publicId}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(combined));
        var hex = Convert.ToHexString(hash)[..16];
        return $"W/\"{size:x}-{versionText:x}-{hex}\"";
    }

    // ---------- public helpers used by the OCR service ----------

    /// <summary>Exposes <see cref="ValidateMetadata"/> to the OCR service.</summary>
    internal static void ValidateMetadataPublic(DocumentType type, string documentNumber, DateOnly? issuedDate, DateOnly? expiryDate)
        => ValidateMetadata(type, documentNumber, issuedDate, expiryDate);

    /// <summary>Exposes <see cref="MapToDto"/> to the OCR service.</summary>
    internal static IdentityDocumentListItemDto MapToDtoPublic(IdentityDocument doc) => MapToDto(doc);
}