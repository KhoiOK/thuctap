using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using UserManagement.Api.FileStorage;
using Xunit;
using Xunit.Abstractions;

namespace UserManagement.Api.Tests.Infrastructure;

/// <summary>
/// End-to-end smoke test of the whole Cloudinary asset lifecycle against the real
/// account: upload (tmp folder) -> move (promote to permanent folder) -> download
/// -> delete. Skipped automatically when no credentials are configured.
/// </summary>
public class CloudinaryLifecycleSmokeTests
{
    private readonly ITestOutputHelper _output;

    public CloudinaryLifecycleSmokeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Upload_Move_Download_Delete_Works()
    {
        var options = new CloudinaryOptions();
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddJsonFile("appsettings.Development.local.json", optional: true)
            .AddEnvironmentVariables()
            .Build()
            .GetSection(CloudinaryOptions.SectionName)
            .Bind(options);

        if (string.IsNullOrWhiteSpace(options.ApiSecret))
        {
            _output.WriteLine("SKIPPED: no Cloudinary credentials configured.");
            return;
        }

        var storage = new CloudinaryStorage(
            new HttpClient(),
            Microsoft.Extensions.Options.Options.Create(options),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CloudinaryStorage>.Instance);

        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

        // 1. Upload into the temporary folder (as API-12 does).
        string tempPublicId;
        using (var source = new MemoryStream(png))
        {
            var uploaded = await storage.UploadAsync("tmp/lifecycle-test", "front", source, "image/png", default);
            tempPublicId = uploaded.PublicId;
            _output.WriteLine($"1. Uploaded   -> {tempPublicId}");
            tempPublicId.Should().StartWith("user-management/tmp/lifecycle-test/front-");
        }

        // 2. The delivery URL must be signed, otherwise Cloudinary answers 401.
        var deliveryUrl = storage.BuildDeliveryUrl(tempPublicId);
        _output.WriteLine($"2. Delivery   -> {deliveryUrl}");
        deliveryUrl.Should().Contain("/image/authenticated/s--");

        var download = await storage.DownloadAsync(tempPublicId, default);
        download.Should().NotBeNull();
        download!.Length.Should().BeGreaterThan(0);
        _output.WriteLine($"3. Downloaded -> {download.Length} bytes, {download.ContentType}");

        // 4. Promote to the permanent folder (as API-13 does).
        var moved = await storage.MoveAsync(tempPublicId, "identity-documents/00000000-0000-0000-0000-000000000000", default);
        _output.WriteLine($"4. Moved     -> {moved.PublicId}");
        moved.PublicId.Should().StartWith("user-management/identity-documents/");

        // 5. The promoted asset must still be downloadable.
        var movedDownload = await storage.DownloadAsync(moved.PublicId, default);
        movedDownload.Should().NotBeNull();
        movedDownload!.Length.Should().BeGreaterThan(0);
        _output.WriteLine($"5. Downloaded -> {movedDownload.Length} bytes");

        // 6. The temporary source must be gone after the move.
        var afterMove = await storage.DownloadAsync(tempPublicId, default);
        afterMove.Should().BeNull();

        // 7. Delete it again.
        var failed = await storage.DeleteAsync([moved.PublicId], default);
        _output.WriteLine($"6. Deleted   -> failed: {failed.Count}");
        failed.Should().BeEmpty();

        // 8. The asset must be gone now.
        var afterDelete = await storage.DownloadAsync(moved.PublicId, default);
        afterDelete.Should().BeNull();
    }
}
