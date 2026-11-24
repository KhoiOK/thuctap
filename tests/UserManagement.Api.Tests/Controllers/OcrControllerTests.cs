using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using UserManagement.Api.DTOs.Users;
using UserManagement.Api.Entities;
using UserManagement.Api.FileStorage;
using UserManagement.Api.Tests.Infrastructure;
using Xunit;

namespace UserManagement.Api.Tests.Controllers;

/// <summary>
/// Tests for the OCR endpoints (API-12 and API-13) driven through the real HTTP
/// pipeline.
/// </summary>
[Collection(nameof(ApiIntegrationCollection))]
public class OcrControllerTests : IAsyncLifetime
{
    private TestApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new TestApiFactory();
        await _factory.InitializeAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    private static byte[] CreateJpeg(int width = 640, int height = 480)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(200, 30, 30));
        using var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder { Quality = 80 });
        return ms.ToArray();
    }

    private async Task<UserDetailDto> CreateUserAsync()
    {
        var req = new UserUpsertRequest
        {
            FullName = "Test User",
            Email = $"user-{Guid.NewGuid():N}@example.com",
            PhoneNumber = "09" + DateTime.UtcNow.Ticks.ToString()[^8..],
            DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = UserManagement.Api.Entities.Gender.Male,
        };
        var response = await _client.PostAsync(
            "/api/v1/users",
            new StringContent(JsonSerializer.Serialize(req, JsonOpts), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDetailDto>(JsonOpts))!;
    }

    /// <summary>
    /// The confirm endpoint must answer 201 with a Location header pointing at the
    /// newly created document (API-13).
    /// </summary>
    [Fact]
    public async Task Confirm_Returns_201_With_A_Location_Header()
    {
        var user = await CreateUserAsync();

        // Seed a scan record and its temporary assets through the service, then call
        // the confirm endpoint over HTTP.
        Guid scanId;
        string frontPublicId;
        string backPublicId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<UserManagement.Api.Data.ApplicationDbContext>();
            var cloudinary = scope.ServiceProvider.GetRequiredService<ICloudinaryStorage>();

            scanId = Guid.NewGuid();

            // Put the assets into the test storage so the existence check passes.
            // The test storage generates the public id, so it must be used as-is.
            var frontUpload = await cloudinary.UploadAsync($"tmp/{scanId:D}", "front", new MemoryStream(CreateJpeg()), "image/jpeg", CancellationToken.None);
            var backUpload = await cloudinary.UploadAsync($"tmp/{scanId:D}", "back", new MemoryStream(CreateJpeg()), "image/jpeg", CancellationToken.None);
            frontPublicId = frontUpload.PublicId;
            backPublicId = backUpload.PublicId;

            db.OcrExtractionResults.Add(new OcrExtractionResult
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ScanId = scanId,
                DocumentType = DocumentType.CCCD,
                Status = OcrScanStatus.Completed,
                FrontImagePublicId = frontPublicId,
                BackImagePublicId = backPublicId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
        }

        var body = new { scanId, documentType = "CCCD", documentNumber = "001098123456" };
        var response = await _client.PostAsync(
            $"/api/v1/users/{user.Id}/identity-documents/ocr/confirm",
            new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json"));

        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, $"body: {text}");
        response.Headers.Location.Should().NotBeNull("the 201 must point at the created document");
        response.Headers.Location!.ToString().Should().Contain($"/api/v1/users/{user.Id}/identity-documents/");
    }
}
