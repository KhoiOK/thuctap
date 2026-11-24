using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using UserManagement.Api.Data;
using UserManagement.Api.DTOs.Users.IdentityDocuments;
using UserManagement.Api.Entities;
using UserManagement.Api.Exceptions;
using UserManagement.Api.FileStorage;
using UserManagement.Api.Services;
using UserManagement.Api.Tests.Infrastructure;
using UserManagement.Api.Validators;
using Xunit;
using ApiIntegrationCollection = UserManagement.Api.Tests.Controllers.ApiIntegrationCollection;

namespace UserManagement.Api.Tests.Services;

/// <summary>
/// Tests for <see cref="IdentityDocumentService"/> that cover the defensive cleanup of
/// Cloudinary assets. A failure in the middle of a multi-image upload must not leave an
/// orphaned (and permanently stored) copy of a PII document on the CDN.
/// </summary>
[Collection(nameof(ApiIntegrationCollection))]
public class IdentityDocumentServiceCleanupTests : IAsyncLifetime
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

    private static ImageValidator CreateValidator()
    {
        var opts = Options.Create(new FileStorageOptions
        {
            RootPath = Path.GetTempPath(),
            MaxFileSizeBytes = 5_000_000,
            MaxPixelSize = 8000
        });
        return new ImageValidator(opts, NullLogger<ImageValidator>.Instance);
    }

    /// <summary>
    /// A Cloudinary double that can be told to fail on the N-th upload, so we can
    /// simulate a partial failure of a multi-file upload.
    /// </summary>
    private sealed class FailingUploadCloudinary : ICloudinaryStorage
    {
        private readonly Dictionary<string, byte[]> _assets = new(StringComparer.Ordinal);
        private readonly int _failOnUploadIndex;

        public FailingUploadCloudinary(int failOnUploadIndex) => _failOnUploadIndex = failOnUploadIndex;

        public bool IsConfigured => true;

        public int UploadCount { get; private set; }

        public IReadOnlyCollection<string> StoredPublicIds => _assets.Keys;

        public Task<CloudinaryUploadResult> UploadAsync(
            string folder, string fileNamePrefix, Stream source, string contentType,
            CancellationToken cancellationToken)
        {
            UploadCount++;
            if (UploadCount == _failOnUploadIndex)
            {
                throw new HttpRequestException("Simulated Cloudinary upload failure.");
            }

            using var buffer = new MemoryStream();
            source.CopyTo(buffer);
            var bytes = buffer.ToArray();
            var publicId = $"{folder}/{fileNamePrefix}-{Guid.NewGuid():N}";
            _assets[publicId] = bytes;

            return Task.FromResult(new CloudinaryUploadResult(publicId, 1, contentType, bytes.Length, 640, 480));
        }

        public Task<CloudinaryUploadResult> MoveAsync(
            string sourcePublicId, string targetFolder, CancellationToken cancellationToken)
            => Task.FromResult(new CloudinaryUploadResult(
                $"{targetFolder}/{sourcePublicId.Split('/').Last()}", 2, "image/jpeg", 1, 640, 480));

        public Task<IReadOnlyList<string>> DeleteAsync(IEnumerable<string> publicIds, CancellationToken cancellationToken)
        {
            var failed = new List<string>();
            foreach (var publicId in publicIds.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct())
            {
                if (!_assets.Remove(publicId))
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

    private static byte[] CreateJpeg(int width = 640, int height = 480)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(255, 0, 0));
        using var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder { Quality = 80 });
        return ms.ToArray();
    }

    private static FormFile CreateFormFile(string name, byte[] bytes, string contentType = "image/jpeg")
        => new(new MemoryStream(bytes), 0, bytes.Length, name, name + ".jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    private async Task<User> SeedUserAsync()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Nguyễn Văn A",
            FullNameSearch = "nguyen van a",
            Email = $"user-{Guid.NewGuid():N}@example.com",
            PhoneNumber = "0912345678",
            CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Create_When_The_Back_Image_Upload_Fails_Deletes_The_Front_Image()
    {
        var user = await SeedUserAsync();

        // The second upload (the back image) fails.
        var cloudinary = new FailingUploadCloudinary(failOnUploadIndex: 2);
        var service = new IdentityDocumentService(
            _db, cloudinary, CreateValidator(), NullLogger<IdentityDocumentService>.Instance, TimeProvider.System);

        var request = new CreateIdentityDocumentRequest
        {
            DocumentType = DocumentType.CCCD,
            DocumentNumber = "001098123456",
            FrontImage = CreateFormFile("frontImage", CreateJpeg()),
            BackImage = CreateFormFile("backImage", CreateJpeg())
        };

        await Assert.ThrowsAnyAsync<Exception>(() => service.CreateAsync(user.Id, request, CancellationToken.None));

        // The front image was uploaded successfully before the failure - it must have
        // been deleted again, otherwise it stays on the CDN forever.
        cloudinary.StoredPublicIds.Should().BeEmpty(
            "the front image uploaded before the failing back image must be cleaned up");
    }

    [Fact]
    public async Task Update_When_The_Back_Image_Upload_Fails_Removes_The_Newly_Uploaded_Front_Image()
    {
        var user = await SeedUserAsync();

        var doc = new IdentityDocument
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DocumentType = DocumentType.CCCD,
            DocumentNumber = "001098123456",
            FrontImagePublicId = "identity-documents/front-old",
            BackImagePublicId = "identity-documents/back-old",
            CreatedAt = DateTime.UtcNow
        };
        _db.IdentityDocuments.Add(doc);
        await _db.SaveChangesAsync();

        // The first upload (the new front image) succeeds, the second one (the new back
        // image) fails.
        var cloudinary = new FailingUploadCloudinary(failOnUploadIndex: 2);
        var service = new IdentityDocumentService(
            _db, cloudinary, CreateValidator(), NullLogger<IdentityDocumentService>.Instance, TimeProvider.System);

        var request = new UpdateIdentityDocumentRequest
        {
            DocumentNumber = "001099999999",
            FrontImage = CreateFormFile("frontImage", CreateJpeg()),
            BackImage = CreateFormFile("backImage", CreateJpeg())
        };

        await Assert.ThrowsAnyAsync<Exception>(() => service.UpdateAsync(user.Id, doc.Id, request, CancellationToken.None));

        // The new front image must be gone.
        cloudinary.StoredPublicIds.Should().BeEmpty("the newly uploaded front image must be cleaned up");

        // The database must still reference the OLD images - a failed update must not
        // leave the document pointing at a deleted asset.
        var reloaded = await _db.IdentityDocuments.AsNoTracking().FirstAsync(d => d.Id == doc.Id);
        reloaded.FrontImagePublicId.Should().Be("identity-documents/front-old");
        reloaded.BackImagePublicId.Should().Be("identity-documents/back-old");
    }

    [Fact]
    public async Task Update_Passport_With_Back_Image_Uploads_Nothing()
    {
        var user = await SeedUserAsync();

        var doc = new IdentityDocument
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DocumentType = DocumentType.Passport,
            DocumentNumber = "A1234567",
            FrontImagePublicId = "identity-documents/front-old",
            CreatedAt = DateTime.UtcNow
        };
        _db.IdentityDocuments.Add(doc);
        await _db.SaveChangesAsync();

        // The upload count is tracked so we can assert that the validation happened
        // BEFORE any network call - the Passport rule must not have side effects.
        var cloudinary = new FailingUploadCloudinary(failOnUploadIndex: 1);
        var service = new IdentityDocumentService(
            _db, cloudinary, CreateValidator(), NullLogger<IdentityDocumentService>.Instance, TimeProvider.System);

        var request = new UpdateIdentityDocumentRequest
        {
            DocumentNumber = "A1234567",
            FrontImage = CreateFormFile("frontImage", CreateJpeg()),
            BackImage = CreateFormFile("backImage", CreateJpeg())
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateAsync(user.Id, doc.Id, request, CancellationToken.None));

        ex.Errors.Should().ContainKey("backImage");
        cloudinary.UploadCount.Should().Be(0, "no image may be uploaded before the validation passed");
        cloudinary.StoredPublicIds.Should().BeEmpty();
    }
}
