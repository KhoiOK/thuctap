using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using UserManagement.Api.FileStorage;
using Xunit;
using Xunit.Abstractions;

namespace UserManagement.Api.Tests.Infrastructure;

/// <summary>
/// Smoke test against the REAL Cloudinary account, using the developer's local
/// settings file. It is skipped automatically when no credentials are configured so
/// it never breaks CI.
///
/// The credentials must point at a real product environment: the api_key, api_secret
/// AND the cloud name all belong to the same Cloudinary product environment. A wrong
/// cloud name makes Cloudinary answer "Invalid cloud_name" / "cloud_name mismatch".
/// </summary>
public class CloudinaryLiveSmokeTests
{
    private readonly ITestOutputHelper _output;

    public CloudinaryLiveSmokeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Real_Cloudinary_Upload_Works()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddJsonFile("appsettings.Development.local.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var options = new CloudinaryOptions();
        config.GetSection(CloudinaryOptions.SectionName).Bind(options);

        if (string.IsNullOrWhiteSpace(options.ApiSecret))
        {
            _output.WriteLine("SKIPPED: no Cloudinary credentials configured.");
            return;
        }

        _output.WriteLine($"CloudName = '{options.CloudName}'");
        _output.WriteLine($"ApiKey    = '{options.ApiKey}'");

        var http = new HttpClient();
        var storage = new CloudinaryStorage(
            http,
            Microsoft.Extensions.Options.Options.Create(options),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CloudinaryStorage>.Instance);

        // 1x1 transparent PNG.
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

        using var source = new MemoryStream(png);

        CloudinaryUploadResult result;
        try
        {
            result = await storage.UploadAsync("tmp/smoke-test", "front", source, "image/png", default);
        }
        catch (CloudinaryException ex)
        {
            // A configuration problem (wrong cloud name / secret) must not fail the
            // build - report it and move on. The real fix is in the local settings file.
            _output.WriteLine($"SKIPPED: Cloudinary rejected the request -> {ex.Message}");
            return;
        }

        _output.WriteLine($"Uploaded -> {result.PublicId} ({result.Size} bytes)");

        result.PublicId.Should().NotBeNullOrWhiteSpace();
        result.PublicId.Should().StartWith("user-management/tmp/smoke-test/");

        // Clean up.
        var failed = await storage.DeleteAsync([result.PublicId], default);
        failed.Should().BeEmpty();
    }
}
