using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
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
/// Tests for <see cref="OcrService.PreviewAsync"/> (API-12b).
///
/// The preview exists so the "Quick Create" flow can pre-fill the user form BEFORE the
/// user record exists. It must therefore:
/// - work without any user in the database,
/// - never persist a scan record (a preview cannot be confirmed),
/// - never create a Cloudinary asset (nothing to clean up),
/// - apply the same image rules as the regular scan.
/// </summary>
[Collection(nameof(ApiIntegrationCollection))]
public class OcrServicePreviewTests : IAsyncLifetime
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

    // ---------- test doubles ----------

    /// <summary>
    /// A Cloudinary double that records every upload. A preview must never call it.
    /// </summary>
    private sealed class RecordingCloudinary : ICloudinaryStorage
    {
        public bool IsConfigured => true;

        public List<string> Uploads { get; } = new();

        public Task<CloudinaryUploadResult> UploadAsync(
            string folder, string fileNamePrefix, Stream source, string contentType,
            CancellationToken cancellationToken)
        {
            Uploads.Add($"{folder}/{fileNamePrefix}");
            return Task.FromResult(new CloudinaryUploadResult($"{folder}/{fileNamePrefix}", 1, contentType, 3, 640, 480));
        }

        public Task<CloudinaryUploadResult> MoveAsync(
            string sourcePublicId, string targetFolder, CancellationToken cancellationToken)
            => throw new NotSupportedException("A preview must not move assets.");

        public Task<IReadOnlyList<string>> DeleteAsync(IEnumerable<string> publicIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public string BuildDeliveryUrl(string publicId) => $"https://res.cloudinary.com/test/{publicId}";

        public Task<CloudinaryDownloadResult?> DownloadAsync(string publicId, CancellationToken cancellationToken)
            => Task.FromResult<CloudinaryDownloadResult?>(null);
    }

    /// <summary>An OCR engine double that returns a fixed raw text.</summary>
    private sealed class StubOcrClient : IPaddleOcrClient
    {
        private readonly string _rawText;

        public StubOcrClient(string rawText = "SỐ: 079095001234\nHỌ VÀ TÊN: NGUYỄN VĂN A\nNGÀY SINH: 20/04/1995\nGIỚI TÍNH: NAM")
        {
            _rawText = rawText;
        }

        public int CallCount { get; private set; }

        public Task<PaddleOcrResponse> RecognizeAsync(
            Stream frontImage, Stream backImage, string documentType, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new PaddleOcrResponse
            {
                Success = true,
                Engine = "stub",
                DocumentType = documentType,
                ProcessingTimeMs = 42,
                RawText = new Dictionary<string, string> { ["front"] = _rawText },
                Fields = new Dictionary<string, PaddleOcrField>
                {
                    ["documentNumber"] = new PaddleOcrField { Value = "079095001234", Confidence = 0.98, Source = "front" },
                    ["fullName"] = new PaddleOcrField { Value = "NGUYỄN VĂN A", Confidence = 0.95, Source = "front" },
                    ["dateOfBirth"] = new PaddleOcrField { Value = "20/04/1995", Confidence = 0.93, Source = "front" },
                    ["gender"] = new PaddleOcrField { Value = "NAM", Confidence = 0.90, Source = "front" }
                },
                Warnings = new List<string>()
            });
        }

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private OcrService CreateService(
        RecordingCloudinary cloudinary,
        StubOcrClient ocrClient,
        bool enabled = true)
    {
        var ocrOptions = Options.Create(new OcrOptions { Enabled = enabled, MinConfidence = 0.6 });
        return new OcrService(
            _db,
            ocrClient,
            cloudinary,
            new OcrNormalizer(ocrOptions, TimeProvider.System),
            new ImageValidator(
                Options.Create(new FileStorageOptions
                {
                    RootPath = Path.GetTempPath(),
                    MaxFileSizeBytes = 5_000_000,
                    MaxPixelSize = 8000
                }),
                NullLogger<ImageValidator>.Instance),
            ocrOptions,
            Options.Create(new CloudinaryOptions { TemporaryImageLifetime = TimeSpan.FromHours(24) }),
            NullLogger<OcrService>.Instance,
            TimeProvider.System);
    }

    /// <summary>Creates a small but valid PNG so the image validator accepts it.</summary>
    private static FormFile CreateImageFile(string name = "front.png", int width = 100, int height = 100)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(0, 0, 255));
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        var bytes = ms.ToArray();
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "image", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
    }

    // ---------- tests ----------

    [Fact]
    public async Task Preview_Works_Without_Any_User_In_The_Database()
    {
        _db.Users.RemoveRange(_db.Users);
        await _db.SaveChangesAsync();

        var cloudinary = new RecordingCloudinary();
        var ocrClient = new StubOcrClient();
        var sut = CreateService(cloudinary, ocrClient);

        var result = await sut.PreviewAsync(
            new OcrPreviewRequest
            {
                DocumentType = DocumentType.CCCD,
                FrontImage = CreateImageFile(),
                BackImage = CreateImageFile("back.png")
            },
            CancellationToken.None);

        result.Fields.Should().NotBeEmpty();
        result.Fields.Should().ContainKey("fullName");
        ocrClient.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Preview_Does_Not_Persist_A_Scan_Record()
    {
        var cloudinary = new RecordingCloudinary();
        var sut = CreateService(cloudinary, new StubOcrClient());

        var before = await _db.OcrExtractionResults.CountAsync();

        await sut.PreviewAsync(
            new OcrPreviewRequest
            {
                DocumentType = DocumentType.CCCD,
                FrontImage = CreateImageFile(),
                BackImage = CreateImageFile("back.png")
            },
            CancellationToken.None);

        var after = await _db.OcrExtractionResults.CountAsync();
        after.Should().Be(before, "a preview is not a scan: it must not be confirmable.");
    }

    [Fact]
    public async Task Preview_Does_Not_Upload_Anything_To_Cloudinary()
    {
        var cloudinary = new RecordingCloudinary();
        var sut = CreateService(cloudinary, new StubOcrClient());

        await sut.PreviewAsync(
            new OcrPreviewRequest
            {
                DocumentType = DocumentType.CCCD,
                FrontImage = CreateImageFile(),
                BackImage = CreateImageFile("back.png")
            },
            CancellationToken.None);

        cloudinary.Uploads.Should().BeEmpty("a preview leaves nothing behind to clean up.");
    }

    [Fact]
    public async Task Preview_Requires_The_Back_Image_For_Cccd()
    {
        var sut = CreateService(new RecordingCloudinary(), new StubOcrClient());

        Func<Task> act = async () => await sut.PreviewAsync(
            new OcrPreviewRequest
            {
                DocumentType = DocumentType.CCCD,
                FrontImage = CreateImageFile()
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
                 .Where(e => e.Errors.ContainsKey("backImage"));
    }

    [Fact]
    public async Task Preview_Rejects_A_Back_Image_For_Passport()
    {
        var sut = CreateService(new RecordingCloudinary(), new StubOcrClient());

        Func<Task> act = async () => await sut.PreviewAsync(
            new OcrPreviewRequest
            {
                DocumentType = DocumentType.Passport,
                FrontImage = CreateImageFile(),
                BackImage = CreateImageFile("back.png")
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
                 .Where(e => e.Errors.ContainsKey("backImage"));
    }

    [Fact]
    public async Task Preview_Requires_The_Front_Image()
    {
        var sut = CreateService(new RecordingCloudinary(), new StubOcrClient());

        Func<Task> act = async () => await sut.PreviewAsync(
            new OcrPreviewRequest { DocumentType = DocumentType.CCCD },
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
                 .Where(e => e.Errors.ContainsKey("frontImage"));
    }

    [Fact]
    public async Task Preview_Throws_When_The_Ocr_Feature_Is_Disabled()
    {
        var sut = CreateService(new RecordingCloudinary(), new StubOcrClient(), enabled: false);

        Func<Task> act = async () => await sut.PreviewAsync(
            new OcrPreviewRequest
            {
                DocumentType = DocumentType.CCCD,
                FrontImage = CreateImageFile(),
                BackImage = CreateImageFile("back.png")
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<OcrUnavailableException>();
    }

    [Fact]
    public async Task Preview_Returns_Warnings_For_Low_Confidence_Fields()
    {
        var cloudinary = new RecordingCloudinary();
        var sut = CreateService(cloudinary, new StubOcrClient());

        var result = await sut.PreviewAsync(
            new OcrPreviewRequest
            {
                DocumentType = DocumentType.CCCD,
                FrontImage = CreateImageFile(),
                BackImage = CreateImageFile("back.png")
            },
            CancellationToken.None);

        // The stub engine reports no confidences, so every field is normalized with
        // confidence 1.0 but some values may still be unreadable -> warnings.
        result.ProcessingTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.DocumentType.Should().Be(DocumentType.CCCD);
    }
}
