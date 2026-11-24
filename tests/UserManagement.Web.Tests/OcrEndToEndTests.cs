using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using UserManagement.Api.Data;
using UserManagement.Api.Entities;
using UserManagement.Web.Tests.Infrastructure;
using Xunit;

namespace UserManagement.Web.Tests;

/// <summary>
/// End-to-end test of the OCR flow: scan (API-12) -&gt; confirm (API-13).
///
/// The scenario covers the fix for the IDOR in the confirm endpoint: the temporary
/// image public ids are taken from the stored scan record, never from the client, and
/// the temporary images are moved into the permanent folder only once the document is
/// actually created.
/// </summary>
[Collection(nameof(WebIntegrationCollection))]
public class OcrEndToEndTests : IAsyncLifetime
{
    private IntegrationTestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = new IntegrationTestHarness();
        await _harness.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null) await _harness.DisposeAsync();
    }

    /// <summary>Generates a valid JPEG for the upload.</summary>
    private static byte[] CreateJpeg(int width = 640, int height = 480)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(200, 30, 30));
        using var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder { Quality = 80 });
        return ms.ToArray();
    }

    private async Task<Guid> CreateUserAsync(string fullName, string email, string phone)
    {
        using var apiHttp = new HttpClient { BaseAddress = _harness.ApiBaseAddress };
        var json = JsonSerializer.Serialize(new
        {
            fullName,
            email,
            phoneNumber = phone,
            dateOfBirth = "1995-04-20",
            gender = "male"
        });
        var response = await apiHttp.PostAsync("/api/v1/users", new StringContent(json, Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"body: {body}");
        using var doc = JsonDocument.Parse(body);
        return Guid.Parse(doc.RootElement.GetProperty("id").GetString()!);
    }

    /// <summary>Runs the OCR scan (API-12) and returns the parsed response.</summary>
    private async Task<JsonDocument> ScanAsync(Guid userId, bool includeBack)
    {
        using var apiHttp = new HttpClient { BaseAddress = _harness.ApiBaseAddress };
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("CCCD"), "documentType");

        var front = new ByteArrayContent(CreateJpeg());
        front.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(front, "frontImage", "front.jpg");

        if (includeBack)
        {
            var back = new ByteArrayContent(CreateJpeg());
            back.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(back, "backImage", "back.jpg");
        }

        var response = await apiHttp.PostAsync(
            $"/api/v1/users/{userId}/identity-documents/ocr/scan", form);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"body: {body}");

        return JsonDocument.Parse(body);
    }

    [Fact]
    public async Task Scan_Then_Confirm_Creates_The_Document_And_Promotes_The_Images()
    {
        var userId = await CreateUserAsync(
            "Nguyễn OCR E2E",
            $"ocr-{Guid.NewGuid():N}@example.com",
            "09" + DateTime.UtcNow.Ticks.ToString()[^8..]);

        // ---- API-12: scan ----
        using var scan = await ScanAsync(userId, includeBack: true);
        var scanId = Guid.Parse(scan.RootElement.GetProperty("scanId").GetString()!);

        // The extracted fields must be present so the form can be auto-filled.
        var fields = scan.RootElement.GetProperty("fields");
        fields.GetProperty("documentNumber").GetProperty("value").GetString().Should().Be("079095001234");
        fields.GetProperty("fullName").GetProperty("value").GetString().Should().Be("NGUYỄN VĂN A");

        // ---- the temporary images live in the tmp folder ----
        using (var scope = _harness.ApiServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var record = await db.OcrExtractionResults
                .AsNoTracking()
                .FirstAsync(r => r.ScanId == scanId);

            record.Status.Should().Be(OcrScanStatus.Completed);
            record.FrontImagePublicId.Should().NotBeNullOrEmpty();
            record.FrontImagePublicId.Should().Contain("tmp/");
            record.BackImagePublicId.Should().NotBeNullOrEmpty();
        }

        // ---- API-13: confirm ----
        using var apiHttp = new HttpClient { BaseAddress = _harness.ApiBaseAddress };
        var confirmBody = JsonSerializer.Serialize(new
        {
            scanId,
            documentType = "CCCD",
            documentNumber = "079095001234"
        });
        var confirmResponse = await apiHttp.PostAsync(
            $"/api/v1/users/{userId}/identity-documents/ocr/confirm",
            new StringContent(confirmBody, Encoding.UTF8, "application/json"));

        var confirmText = await confirmResponse.Content.ReadAsStringAsync();
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.Created, $"body: {confirmText}");

        using var confirmDoc = JsonDocument.Parse(confirmText);
        var documentId = Guid.Parse(confirmDoc.RootElement.GetProperty("id").GetString()!);
        confirmDoc.RootElement.GetProperty("documentNumber").GetString().Should().Be("079095001234");
        confirmDoc.RootElement.GetProperty("hasBackImage").GetBoolean().Should().BeTrue();

        // ---- the document must point at the permanent folder now ----
        using (var scope = _harness.ApiServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var doc = await db.IdentityDocuments.AsNoTracking().FirstAsync(d => d.Id == documentId);
            doc.FrontImagePublicId.Should().Contain($"identity-documents/{userId:D}/");
            doc.FrontImagePublicId.Should().NotContain("tmp/");
            doc.BackImagePublicId.Should().Contain($"identity-documents/{userId:D}/");

            // The scan must be marked as confirmed.
            var scanRecord = await db.OcrExtractionResults.AsNoTracking().FirstAsync(r => r.ScanId == scanId);
            scanRecord.Status.Should().Be(OcrScanStatus.Confirmed);
            scanRecord.DocumentId.Should().Be(documentId);
            scanRecord.ExpiresAt.Should().BeNull();
        }

        // ---- the promoted image must be readable through API-10 ----
        var imageResponse = await apiHttp.GetAsync(
            $"/api/v1/users/{userId}/identity-documents/{documentId}/images/front");
        imageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        imageResponse.Headers.ETag.Should().NotBeNull();
        (await imageResponse.Content.ReadAsByteArrayAsync()).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Confirm_Cannot_Be_Used_Twice_For_The_Same_Scan()
    {
        var userId = await CreateUserAsync(
            "Nguyễn OCR E2E 2",
            $"ocr2-{Guid.NewGuid():N}@example.com",
            "09" + (DateTime.UtcNow.Ticks + 7).ToString()[^8..]);

        using var scan = await ScanAsync(userId, includeBack: true);
        var scanId = Guid.Parse(scan.RootElement.GetProperty("scanId").GetString()!);

        using var apiHttp = new HttpClient { BaseAddress = _harness.ApiBaseAddress };
        var confirmBody = JsonSerializer.Serialize(new
        {
            scanId,
            documentType = "CCCD",
            documentNumber = "079095001234"
        });

        var first = await apiHttp.PostAsync(
            $"/api/v1/users/{userId}/identity-documents/ocr/confirm",
            new StringContent(confirmBody, Encoding.UTF8, "application/json"));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await apiHttp.PostAsync(
            $"/api/v1/users/{userId}/identity-documents/ocr/confirm",
            new StringContent(confirmBody, Encoding.UTF8, "application/json"));
        second.StatusCode.Should().Be(HttpStatusCode.Conflict, "a scan can only be confirmed once");
    }

    [Fact]
    public async Task Confirm_Rejects_A_Scan_Id_Of_Another_User()
    {
        var userA = await CreateUserAsync(
            "Owner A",
            $"owner-a-{Guid.NewGuid():N}@example.com",
            "09" + (DateTime.UtcNow.Ticks + 11).ToString()[^8..]);
        var userB = await CreateUserAsync(
            "Owner B",
            $"owner-b-{Guid.NewGuid():N}@example.com",
            "09" + (DateTime.UtcNow.Ticks + 13).ToString()[^8..]);

        using var scan = await ScanAsync(userA, includeBack: true);
        var scanId = Guid.Parse(scan.RootElement.GetProperty("scanId").GetString()!);

        using var apiHttp = new HttpClient { BaseAddress = _harness.ApiBaseAddress };
        var confirmBody = JsonSerializer.Serialize(new
        {
            scanId,
            documentType = "CCCD",
            documentNumber = "079095001234"
        });

        var response = await apiHttp.PostAsync(
            $"/api/v1/users/{userB}/identity-documents/ocr/confirm",
            new StringContent(confirmBody, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Confirm_Rejects_A_Duplicate_Document_Number()
    {
        var userA = await CreateUserAsync(
            "Dup A",
            $"dupa-{Guid.NewGuid():N}@example.com",
            "09" + (DateTime.UtcNow.Ticks + 17).ToString()[^8..]);
        var userB = await CreateUserAsync(
            "Dup B",
            $"dupb-{Guid.NewGuid():N}@example.com",
            "09" + (DateTime.UtcNow.Ticks + 19).ToString()[^8..]);

        using var apiHttp = new HttpClient { BaseAddress = _harness.ApiBaseAddress };

        foreach (var (userId, number) in new[] { (userA, "001099111111"), (userB, "001099111111") })
        {
            using var scan = await ScanAsync(userId, includeBack: true);
            var scanId = Guid.Parse(scan.RootElement.GetProperty("scanId").GetString()!);

            var confirmBody = JsonSerializer.Serialize(new
            {
                scanId,
                documentType = "CCCD",
                documentNumber = number
            });
            var response = await apiHttp.PostAsync(
                $"/api/v1/users/{userId}/identity-documents/ocr/confirm",
                new StringContent(confirmBody, Encoding.UTF8, "application/json"));

            response.StatusCode.Should().Be(
                userId == userA ? HttpStatusCode.Created : HttpStatusCode.Conflict,
                "BR-11: the (type, number) pair is unique across the whole system");
        }
    }

    [Fact]
    public async Task Scan_Without_A_Back_Image_Is_Rejected_For_Cccd()
    {
        var userId = await CreateUserAsync(
            "No Back",
            $"noback-{Guid.NewGuid():N}@example.com",
            "09" + (DateTime.UtcNow.Ticks + 23).ToString()[^8..]);

        using var apiHttp = new HttpClient { BaseAddress = _harness.ApiBaseAddress };
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("CCCD"), "documentType");
        var front = new ByteArrayContent(CreateJpeg());
        front.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(front, "frontImage", "front.jpg");

        var response = await apiHttp.PostAsync(
            $"/api/v1/users/{userId}/identity-documents/ocr/scan", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "CCCD/CMND require a back image");
    }
}
