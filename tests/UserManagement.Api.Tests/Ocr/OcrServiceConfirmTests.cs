using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UserManagement.Api.Data;
using UserManagement.Api.DTOs.Users.IdentityDocuments;
using UserManagement.Api.Entities;
using UserManagement.Api.Exceptions;
using UserManagement.Api.FileStorage;
using UserManagement.Api.Ocr;
using UserManagement.Api.Services;
using UserManagement.Api.Tests.Infrastructure;
using Xunit;
using ApiIntegrationCollection = UserManagement.Api.Tests.Controllers.ApiIntegrationCollection;

namespace UserManagement.Api.Tests.Ocr;

/// <summary>
/// Tests for the security rules of <see cref="OcrService.ConfirmAsync"/>.
///
/// The confirm endpoint used to accept the Cloudinary public ids of the images from the
/// client. A caller could therefore promote (and thereby read) an arbitrary asset -
/// including one belonging to another user or another scan. The public ids now come
/// exclusively from the stored scan record and are verified against the scan's own
/// temporary folder.
/// </summary>
[Collection(nameof(ApiIntegrationCollection))]
public class OcrServiceConfirmTests : IAsyncLifetime
{
    private TestApiFactory _factory = null!;
    private IServiceScope _scope = null!;
    private ApplicationDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _factory = new TestApiFactory();
        await _factory.InitializeAsync();
        _scope = _factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    public async Task DisposeAsync()
    {
        _scope?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
    }

    /// <summary>
    /// A Cloudinary double that holds a fixed set of assets and can be told to fail a
    /// delete, so the cleanup job's retry behaviour can be observed.
    /// </summary>
    private sealed class StubCloudinary : ICloudinaryStorage
    {
        private readonly Dictionary<string, byte[]> _assets = new(StringComparer.Ordinal);

        public bool IsConfigured => true;

        /// <summary>Public ids whose deletion must be reported as failed.</summary>
        public HashSet<string> FailDeleteFor { get; } = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> StoredPublicIds => _assets.Keys;

        public void Seed(string publicId)
        {
            _assets[publicId] = new byte[] { 1, 2, 3 };
        }

        public Task<CloudinaryUploadResult> UploadAsync(
            string folder, string fileNamePrefix, Stream source, string contentType,
            CancellationToken cancellationToken)
        {
            var publicId = $"{folder}/{fileNamePrefix}-{Guid.NewGuid():N}";
            _assets[publicId] = new byte[] { 1, 2, 3 };
            return Task.FromResult(new CloudinaryUploadResult(publicId, 1, contentType, 3, 640, 480));
        }

        public Task<CloudinaryUploadResult> MoveAsync(
            string sourcePublicId, string targetFolder, CancellationToken cancellationToken)
        {
            var target = $"{targetFolder}/{sourcePublicId.Split('/').Last()}";
            _assets.Remove(sourcePublicId, out var bytes);
            _assets[target] = bytes ?? new byte[] { 1, 2, 3 };
            return Task.FromResult(new CloudinaryUploadResult(target, 2, "image/jpeg", 3, 640, 480));
        }

        public Task<IReadOnlyList<string>> DeleteAsync(IEnumerable<string> publicIds, CancellationToken cancellationToken)
        {
            var failed = new List<string>();
            foreach (var publicId in publicIds.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct())
            {
                if (FailDeleteFor.Contains(publicId) || !_assets.Remove(publicId))
                {
                    failed.Add(publicId);
                }
            }

            return Task.FromResult<IReadOnlyList<string>>(failed);
        }

        public string BuildDeliveryUrl(string publicId) => $"https://res.cloudinary.com/test/{publicId}";

        public Task<CloudinaryDownloadResult?> DownloadAsync(string publicId, CancellationToken cancellationToken)
        {
            if (!_assets.TryGetValue(publicId, out var bytes))
            {
                return Task.FromResult<CloudinaryDownloadResult?>(null);
            }

            return Task.FromResult<CloudinaryDownloadResult?>(
                new CloudinaryDownloadResult(new MemoryStream(bytes), "image/jpeg", bytes.Length, 1));
        }
    }

    /// <summary>An OCR engine double that is never actually called by the confirm flow.</summary>
    private sealed class StubOcrClient : IPaddleOcrClient
    {
        public Task<PaddleOcrResponse> RecognizeAsync(
            Stream frontImage, Stream backImage, string documentType, CancellationToken cancellationToken)
            => throw new NotSupportedException("The confirm flow must not call the OCR engine.");

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private OcrService CreateService(StubCloudinary cloudinary, TimeProvider? time = null)
    {
        return new OcrService(
            _db,
            new StubOcrClient(),
            cloudinary,
            new OcrNormalizer(Options.Create(new OcrOptions { Enabled = true }), TimeProvider.System),
            new UserManagement.Api.FileStorage.ImageValidator(
                Options.Create(new FileStorageOptions
                {
                    RootPath = Path.GetTempPath(),
                    MaxFileSizeBytes = 5_000_000,
                    MaxPixelSize = 8000
                }),
                NullLogger<UserManagement.Api.FileStorage.ImageValidator>.Instance),
            Options.Create(new OcrOptions { Enabled = true, MinConfidence = 0.6 }),
            Options.Create(new CloudinaryOptions { TemporaryImageLifetime = TimeSpan.FromHours(24) }),
            NullLogger<OcrService>.Instance,
            time ?? TimeProvider.System);
    }

    private async Task<User> SeedUserAsync()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Nguyễn Văn A",
            FullNameSearch = "nguyen van a",
            Email = $"user-{Guid.NewGuid():N}@example.com",
            PhoneNumber = $"09{string.Format("{0:D8}", Random.Shared.Next(0, 100_000_000))}",
            CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    /// <summary>Stores a scan record that looks like a real, successful scan.</summary>
    private async Task<OcrExtractionResult> SeedScanAsync(
        Guid userId,
        DocumentType documentType,
        string? frontPublicId,
        string? backPublicId,
        OcrScanStatus status = OcrScanStatus.Completed,
        Guid? scanId = null)
    {
        var scan = new OcrExtractionResult
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ScanId = scanId ?? Guid.NewGuid(),
            DocumentType = documentType,
            Status = status,
            FrontImagePublicId = frontPublicId,
            BackImagePublicId = backPublicId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };
        _db.OcrExtractionResults.Add(scan);
        await _db.SaveChangesAsync();
        return scan;
    }

    [Fact]
    public async Task Confirm_Rejects_A_Scan_Whose_Front_Image_Is_Not_In_The_Scan_Folder()
    {
        var user = await SeedUserAsync();
        var otherScanId = Guid.NewGuid();
        var cloudinary = new StubCloudinary();

        // The scan record points at an asset of a DIFFERENT scan - the classic IDOR.
        var foreignPublicId = $"tmp/{otherScanId:D}/front-abc123";
        cloudinary.Seed(foreignPublicId);
        var scan = await SeedScanAsync(user.Id, DocumentType.CCCD, foreignPublicId, null, scanId: Guid.NewGuid());

        var service = CreateService(cloudinary);
        var request = new OcrConfirmRequest
        {
            ScanId = scan.ScanId,
            DocumentType = DocumentType.CCCD,
            DocumentNumber = "001098123456"
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => service.ConfirmAsync(user.Id, request, CancellationToken.None));

        ex.Errors.Should().ContainKey("frontImage");

        // Nothing must have been created.
        (await _db.IdentityDocuments.CountAsync()).Should().Be(0);
        cloudinary.StoredPublicIds.Should().Contain(foreignPublicId, "the foreign asset must not be moved");
    }

    [Fact]
    public async Task Confirm_Rejects_A_Scan_Whose_Back_Image_Is_Not_In_The_Scan_Folder()
    {
        var user = await SeedUserAsync();
        var scanId = Guid.NewGuid();
        var otherScanId = Guid.NewGuid();
        var cloudinary = new StubCloudinary();

        var frontPublicId = $"tmp/{scanId:D}/front-abc123";
        var foreignBackId = $"tmp/{otherScanId:D}/back-def456";
        cloudinary.Seed(frontPublicId);
        cloudinary.Seed(foreignBackId);

        var scan = await SeedScanAsync(user.Id, DocumentType.CCCD, frontPublicId, foreignBackId, scanId: scanId);

        var service = CreateService(cloudinary);
        var request = new OcrConfirmRequest
        {
            ScanId = scan.ScanId,
            DocumentType = DocumentType.CCCD,
            DocumentNumber = "001098123456"
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => service.ConfirmAsync(user.Id, request, CancellationToken.None));

        ex.Errors.Should().ContainKey("backImage");
        (await _db.IdentityDocuments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Confirm_Rejects_A_Permanent_Asset_That_Is_Not_In_A_Temporary_Folder()
    {
        var user = await SeedUserAsync();
        var cloudinary = new StubCloudinary();

        // A permanently stored (already promoted) asset must never be re-promoted.
        var permanentPublicId = $"identity-documents/{user.Id:D}/{Guid.NewGuid():D}/front-abc123";
        cloudinary.Seed(permanentPublicId);
        var scan = await SeedScanAsync(user.Id, DocumentType.CCCD, permanentPublicId, null, scanId: Guid.NewGuid());

        var service = CreateService(cloudinary);
        var request = new OcrConfirmRequest
        {
            ScanId = scan.ScanId,
            DocumentType = DocumentType.CCCD,
            DocumentNumber = "001098123456"
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => service.ConfirmAsync(user.Id, request, CancellationToken.None));

        ex.Errors.Should().ContainKey("frontImage");
    }

    [Fact]
    public async Task Confirm_Rejects_A_Cccd_Scan_Without_A_Back_Image()
    {
        var user = await SeedUserAsync();
        var scanId = Guid.NewGuid();
        var cloudinary = new StubCloudinary();

        var frontPublicId = $"tmp/{scanId:D}/front-abc123";
        cloudinary.Seed(frontPublicId);
        var scan = await SeedScanAsync(user.Id, DocumentType.CCCD, frontPublicId, null, scanId: scanId);

        var service = CreateService(cloudinary);
        var request = new OcrConfirmRequest
        {
            ScanId = scan.ScanId,
            DocumentType = DocumentType.CCCD,
            DocumentNumber = "001098123456"
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => service.ConfirmAsync(user.Id, request, CancellationToken.None));

        ex.Errors.Should().ContainKey("backImage");
        (await _db.IdentityDocuments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Confirm_Rejects_A_Passport_Scan_That_Has_A_Back_Image()
    {
        var user = await SeedUserAsync();
        var scanId = Guid.NewGuid();
        var cloudinary = new StubCloudinary();

        var frontPublicId = $"tmp/{scanId:D}/front-abc123";
        var backPublicId = $"tmp/{scanId:D}/back-def456";
        cloudinary.Seed(frontPublicId);
        cloudinary.Seed(backPublicId);
        var scan = await SeedScanAsync(user.Id, DocumentType.Passport, frontPublicId, backPublicId, scanId: scanId);

        var service = CreateService(cloudinary);
        var request = new OcrConfirmRequest
        {
            ScanId = scan.ScanId,
            DocumentType = DocumentType.Passport,
            DocumentNumber = "A1234567"
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => service.ConfirmAsync(user.Id, request, CancellationToken.None));

        ex.Errors.Should().ContainKey("backImage");
        (await _db.IdentityDocuments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Confirm_Rejects_A_Scan_Whose_Image_Was_Already_Deleted()
    {
        var user = await SeedUserAsync();
        var scanId = Guid.NewGuid();
        var cloudinary = new StubCloudinary();

        // The asset is referenced by the scan record but no longer exists on Cloudinary
        // (the cleanup job already removed it). A Passport is used so the back-image
        // rule does not fire first.
        var frontPublicId = $"tmp/{scanId:D}/front-abc123";
        var scan = await SeedScanAsync(user.Id, DocumentType.Passport, frontPublicId, null, scanId: scanId);

        var service = CreateService(cloudinary);
        var request = new OcrConfirmRequest
        {
            ScanId = scan.ScanId,
            DocumentType = DocumentType.Passport,
            DocumentNumber = "A1234567"
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => service.ConfirmAsync(user.Id, request, CancellationToken.None));

        ex.Errors.Should().ContainKey("frontImage");
        (await _db.IdentityDocuments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Confirm_Creates_The_Document_For_A_Valid_Scan()
    {
        var user = await SeedUserAsync();
        var scanId = Guid.NewGuid();
        var cloudinary = new StubCloudinary();

        var frontPublicId = $"tmp/{scanId:D}/front-abc123";
        var backPublicId = $"tmp/{scanId:D}/back-def456";
        cloudinary.Seed(frontPublicId);
        cloudinary.Seed(backPublicId);
        var scan = await SeedScanAsync(user.Id, DocumentType.CCCD, frontPublicId, backPublicId, scanId: scanId);

        var service = CreateService(cloudinary);
        var request = new OcrConfirmRequest
        {
            ScanId = scan.ScanId,
            DocumentType = DocumentType.CCCD,
            DocumentNumber = "001098123456"
        };

        var dto = await service.ConfirmAsync(user.Id, request, CancellationToken.None);

        dto.DocumentNumber.Should().Be("001098123456");
        dto.HasBackImage.Should().BeTrue();

        // The assets must have been moved to the permanent folder ...
        cloudinary.StoredPublicIds.Should().NotContain(frontPublicId);
        cloudinary.StoredPublicIds.Should().Contain(id => id.StartsWith($"identity-documents/{user.Id:D}/", StringComparison.Ordinal));

        // ... and the scan must be marked as confirmed.
        var reloaded = await _db.OcrExtractionResults.AsNoTracking().FirstAsync(r => r.ScanId == scan.ScanId);
        reloaded.Status.Should().Be(OcrScanStatus.Confirmed);
        reloaded.DocumentId.Should().Be(dto.Id);
        reloaded.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Confirm_Accepts_A_Scan_Id_Stored_In_The_Compact_Guid_Format()
    {
        var user = await SeedUserAsync();
        var scanId = Guid.NewGuid();
        var cloudinary = new StubCloudinary();

        // The upload path uses the "D" format but the check must also accept "N"
        // (both are valid representations of the same Guid).
        var frontPublicId = $"tmp/{scanId:N}/front-abc123";
        cloudinary.Seed(frontPublicId);
        var scan = await SeedScanAsync(user.Id, DocumentType.Passport, frontPublicId, null, scanId: scanId);

        var service = CreateService(cloudinary);
        var request = new OcrConfirmRequest
        {
            ScanId = scan.ScanId,
            DocumentType = DocumentType.Passport,
            DocumentNumber = "A1234567"
        };

        var dto = await service.ConfirmAsync(user.Id, request, CancellationToken.None);
        dto.DocumentNumber.Should().Be("A1234567");
    }

    [Fact]
    public async Task Confirm_Rejects_A_Scan_Of_Another_User()
    {
        var userA = await SeedUserAsync();
        var userB = await SeedUserAsync();
        var scanId = Guid.NewGuid();
        var cloudinary = new StubCloudinary();

        var frontPublicId = $"tmp/{scanId:D}/front-abc123";
        cloudinary.Seed(frontPublicId);
        var scan = await SeedScanAsync(userA.Id, DocumentType.Passport, frontPublicId, null, scanId: scanId);

        var service = CreateService(cloudinary);
        var request = new OcrConfirmRequest
        {
            ScanId = scan.ScanId,
            DocumentType = DocumentType.Passport,
            DocumentNumber = "A1234567"
        };

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.ConfirmAsync(userB.Id, request, CancellationToken.None));

        (await _db.IdentityDocuments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Confirm_Rejects_An_Already_Confirmed_Scan()
    {
        var user = await SeedUserAsync();
        var scanId = Guid.NewGuid();
        var cloudinary = new StubCloudinary();

        var frontPublicId = $"tmp/{scanId:D}/front-abc123";
        cloudinary.Seed(frontPublicId);
        var scan = await SeedScanAsync(user.Id, DocumentType.Passport, frontPublicId, null, OcrScanStatus.Confirmed, scanId: scanId);

        var service = CreateService(cloudinary);
        var request = new OcrConfirmRequest
        {
            ScanId = scan.ScanId,
            DocumentType = DocumentType.Passport,
            DocumentNumber = "A1234567"
        };

        await Assert.ThrowsAsync<ConflictException>(
            () => service.ConfirmAsync(user.Id, request, CancellationToken.None));
    }

    [Fact]
    public async Task Confirm_Rejects_A_Duplicate_Document_Number_For_Another_User()
    {
        var userA = await SeedUserAsync();
        var userB = await SeedUserAsync();
        var cloudinary = new StubCloudinary();

        var scanIdA = Guid.NewGuid();
        var frontA = $"tmp/{scanIdA:D}/front-a";
        cloudinary.Seed(frontA);
        var scanA = await SeedScanAsync(userA.Id, DocumentType.Passport, frontA, null, scanId: scanIdA);

        var scanIdB = Guid.NewGuid();
        var frontB = $"tmp/{scanIdB:D}/front-b";
        cloudinary.Seed(frontB);
        var scanB = await SeedScanAsync(userB.Id, DocumentType.Passport, frontB, null, scanId: scanIdB);

        var service = CreateService(cloudinary);

        var requestA = new OcrConfirmRequest
        {
            ScanId = scanA.ScanId,
            DocumentType = DocumentType.Passport,
            DocumentNumber = "A1234567"
        };
        await service.ConfirmAsync(userA.Id, requestA, CancellationToken.None);

        // BR-11: the (type, number) pair is unique across the WHOLE system.
        var requestB = new OcrConfirmRequest
        {
            ScanId = scanB.ScanId,
            DocumentType = DocumentType.Passport,
            DocumentNumber = "A1234567"
        };
        await Assert.ThrowsAsync<ConflictException>(
            () => service.ConfirmAsync(userB.Id, requestB, CancellationToken.None));

        (await _db.IdentityDocuments.CountAsync()).Should().Be(1);
    }
}
