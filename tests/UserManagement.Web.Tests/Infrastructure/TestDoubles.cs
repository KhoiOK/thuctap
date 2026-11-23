using UserManagement.Api.FileStorage;
using UserManagement.Api.Ocr;

namespace UserManagement.Web.Tests.Infrastructure;

/// <summary>
/// In-memory <see cref="ICloudinaryStorage"/> used by the end-to-end tests. It behaves
/// like the real implementation but never leaves the process.
/// </summary>
internal sealed class TestCloudinaryStorage : ICloudinaryStorage
{
    private readonly Dictionary<string, byte[]> _assets = new(StringComparer.Ordinal);

    public bool IsConfigured => true;

    public int AssetCount => _assets.Count;

    public Task<CloudinaryUploadResult> UploadAsync(
        string folder,
        string fileNamePrefix,
        Stream source,
        string contentType,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        var bytes = buffer.ToArray();

        var publicId = $"{folder}/{fileNamePrefix}-{Guid.NewGuid():N}";
        _assets[publicId] = bytes;

        return Task.FromResult(new CloudinaryUploadResult(publicId, 1, contentType, bytes.Length, 100, 100));
    }

    public Task<CloudinaryUploadResult> MoveAsync(
        string sourcePublicId,
        string targetFolder,
        CancellationToken cancellationToken)
    {
        var fileName = sourcePublicId.Split('/').Last();
        var targetPublicId = $"{targetFolder}/{fileName}";

        _assets.Remove(sourcePublicId, out var bytes);
        _assets[targetPublicId] = bytes;

        return Task.FromResult(new CloudinaryUploadResult(targetPublicId, 2, "image/jpeg", bytes.Length, 100, 100));
    }

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

    public string BuildDeliveryUrl(string publicId)
        => $"https://res.cloudinary.com/test-cloud/image/authenticated/{publicId}";

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

/// <summary>
/// Fake OCR engine. It returns a fixed set of fields so the end-to-end flow can be
/// asserted without running the real PaddleOCR service.
/// </summary>
internal sealed class FakePaddleOcrClient : IPaddleOcrClient
{
    /// <summary>When set, the client throws <see cref="OcrUnavailableException"/>.</summary>
    public static bool AlwaysUnavailable { get; set; }

    public Task<PaddleOcrResponse> RecognizeAsync(
        Stream frontImage,
        Stream backImage,
        string documentType,
        CancellationToken cancellationToken)
    {
        if (AlwaysUnavailable)
        {
            throw new OcrUnavailableException("Dịch vụ quét tự động đang không khả dụng.");
        }

        var response = new PaddleOcrResponse
        {
            Success = true,
            Engine = "fake",
            DocumentType = documentType,
            ProcessingTimeMs = 42,
            RawText = new Dictionary<string, string>
            {
                ["front"] = "SỐ: 079095001234\nHỌ VÀ TÊN: NGUYỄN VĂN A\nNGÀY SINH: 20/04/1995\nGIỚI TÍNH: NAM\nNGÀY CẤP: 10/05/2021",
                ["back"] = "CÓ GIÁ TRỊ ĐẾN NGÀY: 20/04/2036\nSỐ HIỆU: 012345678"
            },
            Fields = new Dictionary<string, PaddleOcrField>
            {
                ["documentNumber"] = new PaddleOcrField { Value = "079095001234", Confidence = 0.98, Source = "front" },
                ["fullName"] = new PaddleOcrField { Value = "NGUYỄN VĂN A", Confidence = 0.95, Source = "front" },
                ["dateOfBirth"] = new PaddleOcrField { Value = "20/04/1995", Confidence = 0.93, Source = "front" },
                ["gender"] = new PaddleOcrField { Value = "NAM", Confidence = 0.90, Source = "front" },
                ["issueDate"] = new PaddleOcrField { Value = "10/05/2021", Confidence = 0.93, Source = "front" },
                ["expiryDate"] = new PaddleOcrField { Value = "20/04/2036", Confidence = 0.96, Source = "back" }
            }
        };

        return Task.FromResult(response);
    }

    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken) => Task.FromResult(!AlwaysUnavailable);
}
