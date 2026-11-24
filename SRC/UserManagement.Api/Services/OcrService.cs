using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UserManagement.Api.Data;
using UserManagement.Api.DTOs.Users;
using UserManagement.Api.DTOs.Users.IdentityDocuments;
using UserManagement.Api.Entities;
using UserManagement.Api.Exceptions;
using UserManagement.Api.FileStorage;
using UserManagement.Api.Ocr;
using UserManagement.Api.Validators;

namespace UserManagement.Api.Services;

/// <summary>
/// Orchestrates the OCR scanning flow: validate the uploaded images, store them as
/// temporary Cloudinary assets, call the OCR engine, normalize the result and persist the
/// scan for auditing (BR-13, BR-15).
/// </summary>
public interface IOcrService
{
    /// <summary>
    /// Scans the given images and returns the normalized fields. Temporary public ids are
    /// returned so the client can confirm the scan later.
    /// </summary>
    Task<OcrScanResponse> ScanAsync(
        Guid userId,
        OcrScanRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Confirms a previous scan: creates the identity document from the scanned images and
    /// removes the temporary assets.
    /// </summary>
    Task<IdentityDocumentListItemDto> ConfirmAsync(
        Guid userId,
        OcrConfirmRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs the OCR engine against the given images WITHOUT persisting anything and
    /// without requiring an existing user. Used by the Quick Create flow to pre-fill the
    /// user form before the user record exists (API-12b).
    ///
    /// Unlike <see cref="ScanAsync"/>:
    /// - no user is required,
    /// - no temporary Cloudinary asset is created (the engine reads the streams directly),
    /// - no <c>OcrExtractionResult</c> row is written, so the result cannot be confirmed.
    /// </summary>
    Task<OcrPreviewResponse> PreviewAsync(
        OcrPreviewRequest request,
        CancellationToken cancellationToken);
}

public class OcrService : IOcrService
{
    private readonly ApplicationDbContext _db;
    private readonly IPaddleOcrClient _ocrClient;
    private readonly ICloudinaryStorage _cloudinary;
    private readonly OcrNormalizer _normalizer;
    private readonly ImageValidator _imageValidator;
    private readonly OcrOptions _options;
    private readonly CloudinaryOptions _cloudinaryOptions;
    private readonly ILogger<OcrService> _logger;
    private readonly TimeProvider _time;

    public OcrService(
        ApplicationDbContext db,
        IPaddleOcrClient ocrClient,
        ICloudinaryStorage cloudinary,
        OcrNormalizer normalizer,
        ImageValidator imageValidator,
        IOptions<OcrOptions> options,
        IOptions<CloudinaryOptions> cloudinaryOptions,
        ILogger<OcrService> logger,
        TimeProvider time)
    {
        _db = db;
        _ocrClient = ocrClient;
        _cloudinary = cloudinary;
        _normalizer = normalizer;
        _imageValidator = imageValidator;
        _options = options.Value;
        _cloudinaryOptions = cloudinaryOptions.Value;
        _logger = logger;
        _time = time;
    }

    public async Task<OcrScanResponse> ScanAsync(
        Guid userId,
        OcrScanRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            throw new OcrUnavailableException("Tính năng quét tự động đang tắt.");
        }

        await EnsureUserExistsAsync(userId, cancellationToken);

        var documentType = request.DocumentType;
        var requiresBack = documentType is DocumentType.CCCD or DocumentType.CMND;

        // ---- 1. Validate images ----
        await ValidateImageAsync(request.FrontImage, "front", cancellationToken);
        if (request.BackImage is { Length: > 0 })
        {
            if (documentType == DocumentType.Passport)
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["backImage"] = new[] { "Passport không được có ảnh mặt sau." }
                });
            }
            await ValidateImageAsync(request.BackImage, "back", cancellationToken);
        }
        else if (requiresBack)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["backImage"] = new[] { "Ảnh mặt sau là bắt buộc với CCCD/CMND." }
            });
        }

        var scanId = Guid.NewGuid();
        var startedAt = _time.GetUtcNow().UtcDateTime;
        string? frontPublicId = null;
        string? backPublicId = null;

        try
        {
            // ---- 2. Upload to the temporary folder ----
            frontPublicId = await UploadTemporaryAsync(userId, scanId, "front", request.FrontImage!, cancellationToken);
            if (request.BackImage is { Length: > 0 })
            {
                backPublicId = await UploadTemporaryAsync(userId, scanId, "back", request.BackImage, cancellationToken);
            }

            // ---- 3. Call the OCR engine ----
            await using var frontStream = request.FrontImage!.OpenReadStream();
            await using var backStream = request.BackImage is { Length: > 0 }
                ? request.BackImage.OpenReadStream()
                : null;

            var response = await _ocrClient.RecognizeAsync(
                frontStream,
                backStream!,
                documentType.ToString(),
                cancellationToken);

            // ---- 4. Normalize ----
            var confidences = response.Fields?.ToDictionary(
                k => k.Key,
                v => v.Value.Confidence,
                StringComparer.Ordinal) ?? new Dictionary<string, double>();

            var extracted = OcrFieldExtractor.Extract(response.RawText);
            var fields = _normalizer.Normalize(extracted, documentType, confidences);

            // ---- 5. Persist the scan record ----
            var processingTimeMs = (int)(_time.GetUtcNow().UtcDateTime - startedAt).TotalMilliseconds;
            var record = new OcrExtractionResult
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ScanId = scanId,
                DocumentType = documentType,
                Status = OcrScanStatus.Completed,
                FrontImagePublicId = frontPublicId,
                BackImagePublicId = backPublicId,
                RawResult = System.Text.Json.JsonSerializer.Serialize(response),
                ExtractedFields = System.Text.Json.JsonSerializer.Serialize(fields),
                ProcessingTimeMs = processingTimeMs,
                CreatedAt = startedAt,
                ExpiresAt = startedAt.Add(_cloudinaryOptions.TemporaryImageLifetime)
            };

            _db.OcrExtractionResults.Add(record);
            await _db.SaveChangesAsync(cancellationToken);

            return new OcrScanResponse(
                scanId,
                documentType,
                fields,
                BuildWarnings(fields, response.Warnings),
                processingTimeMs);
        }
        catch (OcrUnreadableException ex)
        {
            await CleanupTemporaryAsync(frontPublicId, backPublicId, cancellationToken);
            await RecordFailureAsync(userId, scanId, documentType, frontPublicId, backPublicId, ex.Message, cancellationToken);
            throw;
        }
        catch (OcrUnavailableException ex)
        {
            await CleanupTemporaryAsync(frontPublicId, backPublicId, cancellationToken);
            await RecordFailureAsync(userId, scanId, documentType, frontPublicId, backPublicId, ex.Message, cancellationToken);
            throw;
        }
        catch
        {
            await CleanupTemporaryAsync(frontPublicId, backPublicId, cancellationToken);
            throw;
        }
    }

    public async Task<OcrPreviewResponse> PreviewAsync(
        OcrPreviewRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            throw new OcrUnavailableException("Tính năng quét tự động đang tắt.");
        }

        var documentType = request.DocumentType;
        var requiresBack = documentType is DocumentType.CCCD or DocumentType.CMND;

        // ---- 1. Validate images (same rules as API-12) ----
        await ValidateImageAsync(request.FrontImage, "front", cancellationToken);
        if (request.BackImage is { Length: > 0 })
        {
            if (documentType == DocumentType.Passport)
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["backImage"] = new[] { "Passport không được có ảnh mặt sau." }
                });
            }
            await ValidateImageAsync(request.BackImage, "back", cancellationToken);
        }
        else if (requiresBack)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["backImage"] = new[] { "Ảnh mặt sau là bắt buộc với CCCD/CMND." }
            });
        }

        var startedAt = _time.GetUtcNow().UtcDateTime;

        // ---- 2. Call the OCR engine ----
        // The engine reads the streams directly, so a preview needs no Cloudinary asset
        // and leaves nothing behind to clean up.
        await using var frontStream = request.FrontImage!.OpenReadStream();
        await using var backStream = request.BackImage is { Length: > 0 }
            ? request.BackImage.OpenReadStream()
            : null;

        var response = await _ocrClient.RecognizeAsync(
            frontStream,
            backStream!,
            documentType.ToString(),
            cancellationToken);

        // ---- 3. Normalize ----
        var confidences = response.Fields?.ToDictionary(
            k => k.Key,
            v => v.Value.Confidence,
            StringComparer.Ordinal) ?? new Dictionary<string, double>();

        var extracted = OcrFieldExtractor.Extract(response.RawText);
        var fields = _normalizer.Normalize(extracted, documentType, confidences);

        var processingTimeMs = (int)(_time.GetUtcNow().UtcDateTime - startedAt).TotalMilliseconds;

        return new OcrPreviewResponse(
            documentType,
            fields,
            BuildWarnings(fields, response.Warnings),
            processingTimeMs);
    }

    public async Task<IdentityDocumentListItemDto> ConfirmAsync(
        Guid userId,
        OcrConfirmRequest request,
        CancellationToken cancellationToken)
    {
        var scan = await _db.OcrExtractionResults
            .FirstOrDefaultAsync(r => r.ScanId == request.ScanId && r.UserId == userId, cancellationToken);

        if (scan is null)
        {
            throw new NotFoundException("Phiên quét không tồn tại hoặc đã hết hạn.");
        }

        if (scan.Status == OcrScanStatus.Confirmed)
        {
            throw new ConflictException("scanId", "Phiên quét này đã được xác nhận.");
        }

        if (scan.ExpiresAt is { } expiry && expiry < _time.GetUtcNow().UtcDateTime)
        {
            scan.Status = OcrScanStatus.Expired;
            await _db.SaveChangesAsync(cancellationToken);
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["scanId"] = new[] { "Phiên quét đã hết hạn. Vui lòng quét lại." }
            });
        }

        var documentType = request.DocumentType ?? scan.DocumentType;
        var documentNumber = (request.DocumentNumber ?? string.Empty).Trim();

        // Validate using the same rules as the manual flow (BR-08, BR-10).
        IdentityDocumentService.ValidateMetadataPublic(
            documentType, documentNumber, request.IssuedDate, request.ExpiryDate);

        await EnsureDocumentTypeSlotAvailableAsync(userId, documentType, cancellationToken);
        await EnsureDocumentNumberAvailableAsync(documentType, documentNumber, cancellationToken);

        // ---- Resolve the temporary assets ----
        // The public ids come from the scan record ONLY. The client does not get to
        // choose which asset is promoted: accepting a client-supplied public id would
        // let a caller promote (and thereby read) an arbitrary asset - including one
        // belonging to another user or another scan.
        var frontSource = scan.FrontImagePublicId;
        if (string.IsNullOrWhiteSpace(frontSource))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["frontImage"] = new[] { "Ảnh mặt trước của phiên quét không còn tồn tại." }
            });
        }

        var backSource = scan.BackImagePublicId;

        // ---- Verify the assets really belong to this scan ----
        // The public ids are stored in the temporary folder `tmp/{scanId}` (BR-15).
        // Checking the folder makes sure the scan record cannot point at a permanent
        // (already promoted) asset or at an asset of a different scan.
        //
        // This check runs BEFORE the document-type rules on purpose: it is a security
        // check (is this asset really mine?) and must not be masked by a business rule
        // (do I need a back image?) that fires earlier.
        EnsureBelongsToScan(frontSource, scan.ScanId, "frontImage");
        if (!string.IsNullOrWhiteSpace(backSource))
        {
            EnsureBelongsToScan(backSource, scan.ScanId, "backImage");
        }

        // ---- Enforce the same image rules as the manual flow ----
        // CCCD/CMND must have a back image, Passport must not have one.
        var requiresBack = documentType is DocumentType.CCCD or DocumentType.CMND;
        if (requiresBack && string.IsNullOrWhiteSpace(backSource))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["backImage"] = new[] { "Ảnh mặt sau là bắt buộc với CCCD/CMND." }
            });
        }
        if (documentType == DocumentType.Passport && !string.IsNullOrWhiteSpace(backSource))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["backImage"] = new[] { "Passport không được có ảnh mặt sau." }
            });
        }

        // ---- Check the assets still exist on Cloudinary ----
        // A scan whose temporary images were already deleted (or expired) must not
        // produce a document that points at a missing image.
        if (!await AssetExistsAsync(frontSource, cancellationToken))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["frontImage"] = new[] { "Ảnh mặt trước của phiên quét không còn tồn tại trên máy chủ." }
            });
        }
        if (!string.IsNullOrWhiteSpace(backSource) && !await AssetExistsAsync(backSource, cancellationToken))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["backImage"] = new[] { "Ảnh mặt sau của phiên quét không còn tồn tại trên máy chủ." }
            });
        }

        var documentId = Guid.NewGuid();
        var targetFolder = $"identity-documents/{userId}/{documentId}";

        // Move the temporary assets to their permanent location.
        string frontPublicId;
        long frontVersion = 0;
        string? backPublicId = null;
        long backVersion = 0;

        var movedFront = await _cloudinary.MoveAsync(frontSource, targetFolder, cancellationToken);
        frontPublicId = movedFront.PublicId;
        frontVersion = movedFront.Version;

        if (!string.IsNullOrWhiteSpace(backSource))
        {
            var movedBack = await _cloudinary.MoveAsync(backSource, targetFolder, cancellationToken);
            backPublicId = movedBack.PublicId;
            backVersion = movedBack.Version;
        }

        var doc = new IdentityDocument
        {
            Id = documentId,
            UserId = userId,
            DocumentType = documentType,
            DocumentNumber = NormalizationHelpers.NormalizeDocumentNumber(documentType, documentNumber),
            IssuedDate = request.IssuedDate,
            ExpiryDate = request.ExpiryDate,
            FrontImagePublicId = frontPublicId,
            BackImagePublicId = backPublicId,
            FrontImageVersion = frontVersion,
            BackImageVersion = backVersion,
            CreatedAt = _time.GetUtcNow().UtcDateTime
        };

        _db.IdentityDocuments.Add(doc);
        scan.Status = OcrScanStatus.Confirmed;
        scan.DocumentId = documentId;
        scan.ExpiresAt = null;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // The database write failed - remove the assets we just moved so we do not
            // leave orphaned files behind (mirrors the manual create flow).
            await _cloudinary.DeleteAsync(new[] { frontPublicId, backPublicId }, CancellationToken.None);
            throw;
        }

        return IdentityDocumentService.MapToDtoPublic(doc);
    }
    // =================== helpers ===================

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

    private async Task EnsureDocumentTypeSlotAvailableAsync(Guid userId, DocumentType type, CancellationToken cancellationToken)
    {
        var exists = await _db.IdentityDocuments
            .AnyAsync(d => d.UserId == userId && d.DocumentType == type, cancellationToken);

        if (exists)
        {
            throw new ConflictException("documentType",
                $"Người dùng đã có giấy tờ loại {type}. Mỗi người dùng chỉ có một giấy tờ cho mỗi loại.");
        }
    }

    private async Task EnsureDocumentNumberAvailableAsync(DocumentType type, string documentNumber, CancellationToken cancellationToken)
    {
        var normalized = NormalizationHelpers.NormalizeDocumentNumber(type, documentNumber);
        // BR-11: the (DocumentType, DocumentNumber) pair is unique across the WHOLE
        // system, not per user - same rule as the manual create flow.
        var exists = await _db.IdentityDocuments
            .AnyAsync(d => d.DocumentType == type && d.DocumentNumber == normalized, cancellationToken);

        if (exists)
        {
            throw new ConflictException("documentNumber", "Số giấy tờ đã được đăng ký cho người dùng khác.");
        }
    }

    /// <summary>
    /// Makes sure a temporary public id really lives in the temporary folder of the given
    /// scan (BR-15). Temporary ids look like
    /// <c>{root}/tmp/{scanId}/{side}-{hash}</c>.
    /// </summary>
    internal static void EnsureBelongsToScan(string publicId, Guid scanId, string fieldName)
    {
        var segments = publicId
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // The folder segment "tmp" must be directly followed by the scan id. Comparing
        // segment by segment (instead of matching a substring) makes the check immune
        // to a missing root folder and to a scan id appearing in another position.
        var scanIdTexts = new[] { scanId.ToString("D"), scanId.ToString("N") };

        var belongs = segments
            .Select((segment, index) => (segment, index))
            .Any(x => x.segment.Equals("tmp", StringComparison.OrdinalIgnoreCase)
                      && x.index + 1 < segments.Length
                      && scanIdTexts.Contains(segments[x.index + 1], StringComparer.OrdinalIgnoreCase));

        if (!belongs)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [fieldName] = new[] { "Ảnh không thuộc phiên quét này." }
            });
        }
    }

    /// <summary>
    /// Checks that an asset still exists on Cloudinary. The temporary images may have been
    /// deleted by the cleanup job in the meantime.
    /// </summary>
    private async Task<bool> AssetExistsAsync(string publicId, CancellationToken cancellationToken)
    {
        try
        {
            var downloaded = await _cloudinary.DownloadAsync(publicId, cancellationToken);
            return downloaded is not null;
        }
        catch (Exception ex)
        {
            // A transport error must not block the confirmation - the move below will
            // fail with a proper error message if the asset is really gone.
            _logger.LogWarning(ex, "Could not verify the existence of Cloudinary asset {PublicId}.", publicId);
            return true;
        }
    }

    /// <summary>
    /// Validates the image content (size, magic bytes, dimensions) and enforces the
    /// OCR-specific size limit.
    /// </summary>
    private async Task ValidateImageAsync(
        Microsoft.AspNetCore.Http.IFormFile? file,
        string side,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [$"{side}Image"] = new[] { $"Ảnh mặt {(side == "front" ? "trước" : "sau")} là bắt buộc." }
            });
        }

        if (file.Length > _options.MaxScanImageBytes)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [$"{side}Image"] = new[] { $"Ảnh vượt quá dung lượng tối đa ({_options.MaxScanImageBytes:N0} bytes)." }
            });
        }

        await using var stream = file.OpenReadStream();
        var result = await _imageValidator.ValidateAsync(stream, file.ContentType, cancellationToken);

        if (!result.IsValid)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [$"{side}Image"] = new[] { result.Error ?? "File ảnh không hợp lệ." }
            });
        }
    }

    /// <summary>
    /// Uploads an image into the temporary OCR folder <c>tmp/{scanId}</c> (BR-15).
    /// </summary>
    private async Task<string> UploadTemporaryAsync(
        Guid userId,
        Guid scanId,
        string side,
        Microsoft.AspNetCore.Http.IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var uploaded = await _cloudinary.UploadAsync(
            $"tmp/{scanId}",
            side,
            stream,
            file.ContentType ?? "image/jpeg",
            cancellationToken);

        return uploaded.PublicId;
    }

    /// <summary>
    /// Deletes the temporary assets. Failures are logged but never bubble up (BR-16).
    /// </summary>
    private async Task CleanupTemporaryAsync(string? frontPublicId, string? backPublicId, CancellationToken cancellationToken)
    {
        var ids = new[] { frontPublicId, backPublicId }.Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        try
        {
            var failed = await _cloudinary.DeleteAsync(ids, cancellationToken);
            foreach (var id in failed)
            {
                _logger.LogWarning("Temporary Cloudinary asset {PublicId} could not be deleted.", id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up temporary Cloudinary assets after a failed scan.");
        }
    }

    /// <summary>
    /// Stores a failed scan so the attempt is auditable (BR-13).
    /// </summary>
    private async Task RecordFailureAsync(
        Guid userId,
        Guid scanId,
        DocumentType documentType,
        string? frontPublicId,
        string? backPublicId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            _db.OcrExtractionResults.Add(new OcrExtractionResult
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ScanId = scanId,
                DocumentType = documentType,
                Status = OcrScanStatus.Failed,
                FrontImagePublicId = frontPublicId,
                BackImagePublicId = backPublicId,
                ErrorMessage = errorMessage.Length > 1000 ? errorMessage[..1000] : errorMessage,
                CreatedAt = _time.GetUtcNow().UtcDateTime,
                ExpiresAt = _time.GetUtcNow().UtcDateTime.Add(_cloudinaryOptions.TemporaryImageLifetime)
            });

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist the failed OCR scan record {ScanId}.", scanId);
        }
    }

    /// <summary>
    /// Builds the user-facing warnings. Values of OCR fields are PII and must never be
    /// written to the log, so only the field name and the confidence are included.
    /// </summary>
    private List<string> BuildWarnings(Dictionary<string, OcrField> fields, List<string>? engineWarnings)
    {
        var warnings = new List<string>();

        foreach (var (name, field) in fields)
        {
            if (!field.HasValue)
            {
                warnings.Add($"Không đọc được \"{FieldDisplayName(name)}\" trên ảnh.");
                continue;
            }

            if (!field.IsValid)
            {
                warnings.Add($"\"{FieldDisplayName(name)}\" không hợp lệ theo quy tắc nghiệp vụ, vui lòng kiểm tra lại.");
                continue;
            }

            if (field.Confidence < _options.MinConfidence)
            {
                warnings.Add($"\"{FieldDisplayName(name)}\" chỉ đạt {field.Confidence:P0} độ tin cậy, chưa được điền tự động.");
                continue;
            }

            if (field.IsInferred)
            {
                warnings.Add($"\"{FieldDisplayName(name)}\" được suy luận từ năm 2 chữ số, vui lòng kiểm tra lại.");
            }
        }

        if (engineWarnings is not null)
        {
            warnings.AddRange(engineWarnings);
        }

        return warnings;
    }

    private static string FieldDisplayName(string field) => field switch
    {
        "documentNumber" => "Số giấy tờ",
        "fullName" => "Họ và tên",
        "dateOfBirth" => "Ngày sinh",
        "gender" => "Giới tính",
        "address" => "Địa chỉ",
        "issueDate" => "Ngày cấp",
        "expiryDate" => "Ngày hết hạn",
        "authorityNo" => "Số hiệu",
        _ => field
    };
}
