using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UserManagement.Api.Data;
using UserManagement.Api.Entities;
using UserManagement.Api.FileStorage;
using UserManagement.Api.Ocr;
using UserManagement.Api.Tests.Infrastructure;
using Xunit;
using ApiIntegrationCollection = UserManagement.Api.Tests.Controllers.ApiIntegrationCollection;

namespace UserManagement.Api.Tests.Ocr;

/// <summary>
/// Tests for <see cref="OcrCleanupService"/>. The background job removes the temporary
/// OCR images once they expire (BR-15, BR-16).
///
/// The critical rule: a scan whose assets could NOT be deleted must keep its expiry
/// date so the next run retries the deletion. Marking it as expired while the image is
/// still on the CDN would leak the PII image forever.
/// </summary>
[Collection(nameof(ApiIntegrationCollection))]
public class OcrCleanupServiceTests : IAsyncLifetime
{
    private TestApiFactory _factory = null!;
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        _factory = new TestApiFactory();
        await _factory.InitializeAsync();
        _connectionString =
            $"Server=localhost;Database={_factory.DatabaseName};Trusted_Connection=True;TrustServerCertificate=True";
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
    }

    /// <summary>
    /// A Cloudinary double whose deletions can be made to fail for specific public ids.
    /// </summary>
    private sealed class StubCloudinary : ICloudinaryStorage
    {
        private readonly Dictionary<string, byte[]> _assets = new(StringComparer.Ordinal);

        public bool IsConfigured => true;

        /// <summary>Public ids whose deletion must be reported as failed.</summary>
        public HashSet<string> FailDeleteFor { get; } = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> StoredPublicIds => _assets.Keys;

        public void Seed(string publicId) => _assets[publicId] = new byte[] { 1, 2, 3 };

        public Task<CloudinaryUploadResult> UploadAsync(
            string folder, string fileNamePrefix, Stream source, string contentType,
            CancellationToken cancellationToken)
            => Task.FromResult(new CloudinaryUploadResult(
                $"{folder}/{fileNamePrefix}", 1, contentType, 3, 640, 480));

        public Task<CloudinaryUploadResult> MoveAsync(
            string sourcePublicId, string targetFolder, CancellationToken cancellationToken)
            => Task.FromResult(new CloudinaryUploadResult(
                $"{targetFolder}/{sourcePublicId.Split('/').Last()}", 2, "image/jpeg", 3, 640, 480));

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
            => Task.FromResult<CloudinaryDownloadResult?>(null);
    }

    /// <summary>
    /// Builds a service provider that points at the same database as the factory but
    /// uses the stub Cloudinary storage, so the cleanup job can be driven directly.
    /// </summary>
    private ServiceProvider BuildProvider(StubCloudinary cloudinary)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICloudinaryStorage>(cloudinary);
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(_connectionString));
        return services.BuildServiceProvider();
    }

    private OcrCleanupService CreateService(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new CloudinaryOptions { TemporaryImageLifetime = TimeSpan.FromHours(24) }),
            NullLogger<OcrCleanupService>.Instance);

    private async Task<User> SeedUserAsync(ApplicationDbContext db)
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
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<OcrExtractionResult> SeedExpiredScanAsync(
        ApplicationDbContext db,
        Guid userId,
        string? frontPublicId,
        string? backPublicId,
        OcrScanStatus status = OcrScanStatus.Completed)
    {
        var scan = new OcrExtractionResult
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ScanId = Guid.NewGuid(),
            DocumentType = DocumentType.CCCD,
            Status = status,
            FrontImagePublicId = frontPublicId,
            BackImagePublicId = backPublicId,
            CreatedAt = DateTime.UtcNow.AddHours(-30),
            ExpiresAt = DateTime.UtcNow.AddHours(-6) // already expired
        };
        db.OcrExtractionResults.Add(scan);
        await db.SaveChangesAsync();
        return scan;
    }

    [Fact]
    public async Task Cleanup_Deletes_The_Assets_And_Expires_The_Scan()
    {
        var cloudinary = new StubCloudinary();
        var front = $"tmp/{Guid.NewGuid():D}/front-a";
        var back = $"tmp/{Guid.NewGuid():D}/back-b";
        cloudinary.Seed(front);
        cloudinary.Seed(back);

        await using var provider = BuildProvider(cloudinary);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await SeedUserAsync(db);
        var scan = await SeedExpiredScanAsync(db, user.Id, front, back);

        var service = CreateService(provider);
        await service.CleanupExpiredScansAsync(CancellationToken.None);

        var reloaded = await db.OcrExtractionResults.AsNoTracking().FirstAsync(r => r.Id == scan.Id);
        reloaded.Status.Should().Be(OcrScanStatus.Expired);
        reloaded.ExpiresAt.Should().BeNull("a successfully cleaned scan must not be retried");

        // The assets must actually be gone.
        cloudinary.StoredPublicIds.Should().NotContain(front);
        cloudinary.StoredPublicIds.Should().NotContain(back);
    }

    [Fact]
    public async Task Cleanup_Keeps_The_Expiry_Date_When_The_Delete_Fails()
    {
        var cloudinary = new StubCloudinary();
        var front = $"tmp/{Guid.NewGuid():D}/front-a";
        var back = $"tmp/{Guid.NewGuid():D}/back-b";
        cloudinary.Seed(front);
        cloudinary.Seed(back);

        // The front image cannot be deleted.
        cloudinary.FailDeleteFor.Add(front);

        await using var provider = BuildProvider(cloudinary);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await SeedUserAsync(db);
        var scan = await SeedExpiredScanAsync(db, user.Id, front, back);

        var service = CreateService(provider);
        await service.CleanupExpiredScansAsync(CancellationToken.None);

        var reloaded = await db.OcrExtractionResults.AsNoTracking().FirstAsync(r => r.Id == scan.Id);

        // The record must NOT be marked as expired and must KEEP its expiry date so the
        // next run of the job retries the deletion.
        reloaded.Status.Should().Be(OcrScanStatus.Completed);
        reloaded.ExpiresAt.Should().NotBeNull("a failed delete must be retried on the next run");
        reloaded.ExpiresAt.Should().Be(scan.ExpiresAt);
    }

    [Fact]
    public async Task Cleanup_Retries_A_Previously_Failed_Delete_On_The_Next_Run()
    {
        var cloudinary = new StubCloudinary();
        var front = $"tmp/{Guid.NewGuid():D}/front-a";
        cloudinary.Seed(front);

        await using var provider = BuildProvider(cloudinary);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await SeedUserAsync(db);
        var scan = await SeedExpiredScanAsync(db, user.Id, front, null);

        var service = CreateService(provider);

        // --- Run 1: the delete fails ---
        cloudinary.FailDeleteFor.Add(front);
        await service.CleanupExpiredScansAsync(CancellationToken.None);

        var afterFirstRun = await db.OcrExtractionResults.AsNoTracking().FirstAsync(r => r.Id == scan.Id);
        afterFirstRun.Status.Should().Be(OcrScanStatus.Completed);
        afterFirstRun.ExpiresAt.Should().NotBeNull();

        // --- Run 2: Cloudinary is healthy again ---
        cloudinary.FailDeleteFor.Clear();
        await service.CleanupExpiredScansAsync(CancellationToken.None);

        var afterSecondRun = await db.OcrExtractionResults.AsNoTracking().FirstAsync(r => r.Id == scan.Id);
        afterSecondRun.Status.Should().Be(OcrScanStatus.Expired);
        afterSecondRun.ExpiresAt.Should().BeNull();
        cloudinary.StoredPublicIds.Should().NotContain(front);
    }

    [Fact]
    public async Task Cleanup_Ignores_Scans_That_Have_Not_Expired_Yet()
    {
        var cloudinary = new StubCloudinary();
        var front = $"tmp/{Guid.NewGuid():D}/front-a";
        cloudinary.Seed(front);

        await using var provider = BuildProvider(cloudinary);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await SeedUserAsync(db);
        var scan = await SeedExpiredScanAsync(db, user.Id, front, null);
        scan.ExpiresAt = DateTime.UtcNow.AddHours(6);
        await db.SaveChangesAsync();

        var service = CreateService(provider);
        await service.CleanupExpiredScansAsync(CancellationToken.None);

        var reloaded = await db.OcrExtractionResults.AsNoTracking().FirstAsync(r => r.Id == scan.Id);
        reloaded.Status.Should().Be(OcrScanStatus.Completed);
        reloaded.ExpiresAt.Should().NotBeNull();
        cloudinary.StoredPublicIds.Should().Contain(front, "a scan that has not expired must keep its image");
    }
}
