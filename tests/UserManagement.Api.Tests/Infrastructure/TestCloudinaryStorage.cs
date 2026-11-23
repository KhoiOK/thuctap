using UserManagement.Api.FileStorage;

namespace UserManagement.Api.Tests.Infrastructure;

/// <summary>
/// In-memory <see cref="ICloudinaryStorage"/> used by the tests. It records the uploaded
/// assets so the tests can assert on them without contacting Cloudinary.
/// </summary>
internal sealed class TestCloudinaryStorage : ICloudinaryStorage
{
    private readonly Dictionary<string, byte[]> _assets = new(StringComparer.Ordinal);
    private readonly List<string> _deleted = new();

    public bool IsConfigured => true;

    /// <summary>Public ids that were deleted through this instance.</summary>
    public IReadOnlyList<string> DeletedAssets => _deleted;

    /// <summary>Number of assets currently held.</summary>
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

        return Task.FromResult(new CloudinaryUploadResult(
            publicId,
            1,
            contentType,
            bytes.Length,
            100,
            100));
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
            if (_assets.Remove(publicId))
            {
                _deleted.Add(publicId);
            }
            else
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

        var stream = new MemoryStream(bytes);
        return Task.FromResult<CloudinaryDownloadResult?>(
            new CloudinaryDownloadResult(stream, "image/jpeg", bytes.Length, 1));
    }
}
