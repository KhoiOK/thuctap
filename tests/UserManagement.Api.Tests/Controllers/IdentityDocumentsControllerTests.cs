using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using UserManagement.Api.DTOs.Users;
using UserManagement.Api.DTOs.Users.IdentityDocuments;
using UserManagement.Api.Entities;
using UserManagement.Api.Tests.Infrastructure;
using Xunit;

namespace UserManagement.Api.Tests.Controllers;

/// <summary>
/// End-to-end tests for the IdentityDocument endpoints (API-06..API-11). Exercises
/// multipart upload, ETag/If-None-Match handling, and file cleanup on delete.
/// </summary>
[Collection(nameof(ApiIntegrationCollection))]
public class IdentityDocumentsControllerTests : IAsyncLifetime
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

    private string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";
    private string UniquePhone() => $"09{string.Format("{0:D8}", Random.Shared.Next(0, 100_000_000))}";

    private async Task<UserDetailDto> CreateUserAsync()
    {
        var req = new UserUpsertRequest
        {
            FullName = "Test User",
            Email = UniqueEmail(),
            PhoneNumber = UniquePhone(),
            DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = Gender.Male,
        };
        var json = JsonSerializer.Serialize(req, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/v1/users", content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDetailDto>(JsonOpts))!;
    }

    private static MultipartFormDataContent BuildCccdForm(
        string documentNumber,
        bool withBackImage = true,
        DateOnly? issuedDate = null,
        DateOnly? expiryDate = null,
        bool frontIsJpeg = true)
    {
        return BuildForm(
            "CCCD", documentNumber, issuedDate, expiryDate,
            withBackImage, withBackImage, frontIsJpeg);
    }

    private static MultipartFormDataContent BuildPassportForm(string documentNumber, bool withBackImage = false)
    {
        return BuildForm(
            "Passport", documentNumber, null, null,
            withBackImage: withBackImage, backIsJpeg: true, frontIsJpeg: true);
    }

    private static MultipartFormDataContent BuildForm(
        string documentType,
        string documentNumber,
        DateOnly? issuedDate,
        DateOnly? expiryDate,
        bool withBackImage,
        bool backIsJpeg,
        bool frontIsJpeg)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(documentType), "documentType");
        form.Add(new StringContent(documentNumber), "documentNumber");
        if (issuedDate is { } i)
            form.Add(new StringContent(i.ToString("yyyy-MM-dd")), "issuedDate");
        if (expiryDate is { } e)
            form.Add(new StringContent(e.ToString("yyyy-MM-dd")), "expiryDate");

        var frontBytes = CreateImageBytes(640, 480, frontIsJpeg);
        var frontContent = new ByteArrayContent(frontBytes);
        frontContent.Headers.ContentType = new MediaTypeHeaderValue(frontIsJpeg ? "image/jpeg" : "image/png");
        form.Add(frontContent, "frontImage", "front.jpg");

        if (withBackImage)
        {
            var backBytes = CreateImageBytes(640, 480, backIsJpeg);
            var backContent = new ByteArrayContent(backBytes);
            backContent.Headers.ContentType = new MediaTypeHeaderValue(backIsJpeg ? "image/jpeg" : "image/png");
            form.Add(backContent, "backImage", "back.jpg");
        }

        return form;
    }

    /// <summary>Generates a valid 640x480 JPEG/PNG of solid color.</summary>
    private static byte[] CreateImageBytes(int width, int height, bool jpeg)
    {
        // Rgba32 has an indexer that supports per-pixel writes; for a solid fill we just
        // allocate the image (default is transparent) and fill a corner pixel.
        using var image = new Image<Rgba32>(width, height, new Rgba32(255, 0, 0));

        using var ms = new MemoryStream();
        if (jpeg)
        {
            image.Save(ms, new JpegEncoder { Quality = 80 });
        }
        else
        {
            image.Save(ms, new PngEncoder());
        }
        return ms.ToArray();
    }

    // ============== API-07 create ==============

    [Fact]
    public async Task Create_Cccd_With_Front_And_Back_Returns_201()
    {
        var user = await CreateUserAsync();
        var form = BuildCccdForm("001098123456", withBackImage: true);

        var response = await _client.PostAsync($"/api/v1/users/{user.Id}/identity-documents", form);
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, $"body was: {raw}");
        response.Headers.Location.Should().NotBeNull();

        var dto = await response.Content.ReadFromJsonAsync<IdentityDocumentListItemDto>(JsonOpts);
        dto!.DocumentNumber.Should().Be("001098123456");
        dto.DocumentType.Should().Be(DocumentType.CCCD);
        dto.HasBackImage.Should().BeTrue();
        dto.BackImageUrl.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_Cccd_Missing_Back_Returns_400()
    {
        var user = await CreateUserAsync();
        var form = BuildCccdForm("001098123456", withBackImage: false);

        var response = await _client.PostAsync($"/api/v1/users/{user.Id}/identity-documents", form);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_Passport_Without_Back_Is_Allowed()
    {
        var user = await CreateUserAsync();
        var form = BuildPassportForm("A1234567", withBackImage: false);

        var response = await _client.PostAsync($"/api/v1/users/{user.Id}/identity-documents", form);
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, $"body: {raw}");

        var dto = await response.Content.ReadFromJsonAsync<IdentityDocumentListItemDto>(JsonOpts);
        dto!.DocumentType.Should().Be(DocumentType.Passport);
        dto.HasBackImage.Should().BeFalse();
        dto.BackImageUrl.Should().BeNull();
    }

    [Fact]
    public async Task Create_Passport_With_Back_Returns_400()
    {
        var user = await CreateUserAsync();
        var form = BuildPassportForm("A1234567", withBackImage: true);

        var response = await _client.PostAsync($"/api/v1/users/{user.Id}/identity-documents", form);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_Bad_Document_Number_Returns_400()
    {
        var user = await CreateUserAsync();
        var form = BuildCccdForm("12"); // 2 digits - not valid for CCCD
        var response = await _client.PostAsync($"/api/v1/users/{user.Id}/identity-documents", form);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_Duplicate_Type_Returns_409()
    {
        var user = await CreateUserAsync();

        var first = await _client.PostAsync($"/api/v1/users/{user.Id}/identity-documents", BuildCccdForm("001098123456"));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await _client.PostAsync($"/api/v1/users/{user.Id}/identity-documents", BuildCccdForm("001099123456"));
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_Duplicate_Document_Number_Across_Users_Returns_409()
    {
        var userA = await CreateUserAsync();
        var userB = await CreateUserAsync();

        var first = await _client.PostAsync($"/api/v1/users/{userA.Id}/identity-documents", BuildCccdForm("001098123456"));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // userB tries the same (type, number) tuple - must 409 because the unique index is on (type, number).
        var second = await _client.PostAsync($"/api/v1/users/{userB.Id}/identity-documents", BuildCccdForm("001098123456"));
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_Same_Document_Number_Across_Different_Types_Is_Allowed()
    {
        var user = await CreateUserAsync();

        var first = await _client.PostAsync($"/api/v1/users/{user.Id}/identity-documents", BuildCccdForm("001098123456"));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // A different type is a different unique slot, so the same 12-digit number on CMND
        // (which accepts 9 or 12 digits) is allowed.
        var form = BuildForm("CMND", "001098123456", null, null, withBackImage: true, backIsJpeg: true, frontIsJpeg: true);
        var second = await _client.PostAsync($"/api/v1/users/{user.Id}/identity-documents", form);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_NonImage_Front_Returns_400()
    {
        var user = await CreateUserAsync();
        var form = new MultipartFormDataContent();
        form.Add(new StringContent("CCCD"), "documentType");
        form.Add(new StringContent("001098123456"), "documentNumber");
        var bogus = new ByteArrayContent(Encoding.UTF8.GetBytes("not an image"));
        bogus.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(bogus, "frontImage", "front.jpg");
        // No back - we expect front validation to fail first.
        var response = await _client.PostAsync($"/api/v1/users/{user.Id}/identity-documents", form);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_Unknown_User_Returns_404()
    {
        var form = BuildCccdForm("001098123456");
        var response = await _client.PostAsync($"/api/v1/users/{Guid.NewGuid()}/identity-documents", form);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ============== API-06 list ==============

    [Fact]
    public async Task List_Returns_Empty_When_No_Documents()
    {
        var user = await CreateUserAsync();
        var response = await _client.GetAsync($"/api/v1/users/{user.Id}/identity-documents");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<List<IdentityDocumentListItemDto>>(JsonOpts);
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task List_Returns_All_Documents_For_User()
    {
        var user = await CreateUserAsync();
        await _client.PostAsync($"/api/v1/users/{user.Id}/identity-documents", BuildCccdForm("001098123456"));
        await _client.PostAsync($"/api/v1/users/{user.Id}/identity-documents", BuildPassportForm("A1234567"));

        var response = await _client.GetAsync($"/api/v1/users/{user.Id}/identity-documents");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = (await response.Content.ReadFromJsonAsync<List<IdentityDocumentListItemDto>>(JsonOpts))!;
        list.Should().HaveCount(2);
        list.Select(d => d.DocumentType).Should().BeEquivalentTo(new[] { DocumentType.CCCD, DocumentType.Passport });
    }

    // ============== API-11 get ==============

    [Fact]
    public async Task GetById_Returns_Single_Document()
    {
        var user = await CreateUserAsync();
        var created = await (await _client.PostAsync($"/api/v1/users/{user.Id}/identity-documents", BuildCccdForm("001098123456")))
            .Content.ReadFromJsonAsync<IdentityDocumentListItemDto>(JsonOpts);

        var response = await _client.GetAsync($"/api/v1/users/{user.Id}/identity-documents/{created!.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<IdentityDocumentListItemDto>(JsonOpts);
        dto!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetById_Wrong_User_Returns_404()
    {
        var userA = await CreateUserAsync();
        var userB = await CreateUserAsync();
        var created = await (await _client.PostAsync($"/api/v1/users/{userA.Id}/identity-documents", BuildCccdForm("001098123456")))
            .Content.ReadFromJsonAsync<IdentityDocumentListItemDto>(JsonOpts);

        var response = await _client.GetAsync($"/api/v1/users/{userB.Id}/identity-documents/{created!.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ============== API-08 update ==============

    [Fact]
    public async Task Update_Metadata_Works_Without_Image_Upload()
    {
        var user = await CreateUserAsync();
        var created = await (await _client.PostAsync($"/api/v1/users/{user.Id}/identity-documents", BuildCccdForm("001098123456")))
            .Content.ReadFromJsonAsync<IdentityDocumentListItemDto>(JsonOpts);

        var form = new MultipartFormDataContent();
        form.Add(new StringContent("001099999999"), "documentNumber");

        var response = await _client.PutAsync($"/api/v1/users/{user.Id}/identity-documents/{created!.Id}", form);
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"body: {raw}");
        var dto = await response.Content.ReadFromJsonAsync<IdentityDocumentListItemDto>(JsonOpts);
        dto!.DocumentNumber.Should().Be("001099999999");
    }

    // ============== API-09 delete ==============

    [Fact]
    public async Task Delete_Document_Returns_204_And_Removes_It()
    {
        var user = await CreateUserAsync();
        var created = await (await _client.PostAsync($"/api/v1/users/{user.Id}/identity-documents", BuildCccdForm("001098123456")))
            .Content.ReadFromJsonAsync<IdentityDocumentListItemDto>(JsonOpts);

        var del = await _client.DeleteAsync($"/api/v1/users/{user.Id}/identity-documents/{created!.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await _client.GetAsync($"/api/v1/users/{user.Id}/identity-documents/{created.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_Unknown_Document_Returns_404()
    {
        var user = await CreateUserAsync();
        var response = await _client.DeleteAsync($"/api/v1/users/{user.Id}/identity-documents/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ============== API-10 image ==============

    [Fact]
    public async Task Get_Front_Image_Returns_200_With_Body_And_Etag()
    {
        var user = await CreateUserAsync();
        var created = await (await _client.PostAsync($"/api/v1/users/{user.Id}/identity-documents", BuildCccdForm("001098123456")))
            .Content.ReadFromJsonAsync<IdentityDocumentListItemDto>(JsonOpts);

        var response = await _client.GetAsync($"/api/v1/users/{user.Id}/identity-documents/{created!.Id}/images/front");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        response.Headers.ETag.Should().NotBeNull();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Get_Image_With_Matching_Etag_Returns_304()
    {
        var user = await CreateUserAsync();
        var created = await (await _client.PostAsync($"/api/v1/users/{user.Id}/identity-documents", BuildCccdForm("001098123456")))
            .Content.ReadFromJsonAsync<IdentityDocumentListItemDto>(JsonOpts);

        var first = await _client.GetAsync($"/api/v1/users/{user.Id}/identity-documents/{created!.Id}/images/front");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        // Use ToString() so the request header includes the surrounding quotes.
        var etagValue = first.Headers.ETag!.ToString();

        var second = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/users/{user.Id}/identity-documents/{created.Id}/images/front");
        second.Headers.TryAddWithoutValidation("If-None-Match", etagValue);
        var secondResponse = await _client.SendAsync(second);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Get_Image_Invalid_Side_Returns_404()
    {
        var user = await CreateUserAsync();
        var created = await (await _client.PostAsync($"/api/v1/users/{user.Id}/identity-documents", BuildCccdForm("001098123456")))
            .Content.ReadFromJsonAsync<IdentityDocumentListItemDto>(JsonOpts);

        var response = await _client.GetAsync($"/api/v1/users/{user.Id}/identity-documents/{created!.Id}/images/side");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ============== helpers ==============
}
