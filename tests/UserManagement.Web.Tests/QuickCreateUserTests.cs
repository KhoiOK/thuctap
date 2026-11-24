using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using UserManagement.Api.Data;
using UserManagement.Web.Tests.Infrastructure;
using Xunit;

namespace UserManagement.Web.Tests;

/// <summary>
/// End-to-end tests for the "Thêm User nhanh" (Quick Create) flow.
///
/// The flow creates the user first and the identity document second, because the API
/// always owns an identity document by an existing user (foreign key). The tests cover
/// the business rules that matter most:
/// - a user can be created without any OCR,
/// - the OCR preview never persists anything,
/// - the OCR preview works before the user exists,
/// - a failing document step keeps the user (no duplicate user is created).
/// </summary>
[Collection(nameof(WebIntegrationCollection))]
public class QuickCreateUserTests : IAsyncLifetime
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

    private static string ExtractAntiforgeryToken(string html)
    {
        var marker = "name=\"__RequestVerificationToken\"";
        var idx = html.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) throw new InvalidOperationException("No antiforgery token in form HTML.");
        var valueStart = html.IndexOf("value=\"", idx, StringComparison.Ordinal) + "value=\"".Length;
        var valueEnd = html.IndexOf('"', valueStart);
        return html.Substring(valueStart, valueEnd - valueStart);
    }

    /// <summary>Reads the form and returns the anti-forgery token.</summary>
    private async Task<string> GetTokenAsync(string path)
    {
        var response = await _harness.WebClient.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        return ExtractAntiforgeryToken(html);
    }

    // ---------- the page itself ----------

    [Fact]
    public async Task QuickCreate_Page_Renders_Both_Input_Modes()
    {
        var response = await _harness.WebClient.GetAsync("/Users/QuickCreate");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("data-mode-option=\"manual\"");
        html.Should().Contain("data-mode-option=\"ocr\"");
        html.Should().Contain("qc-mode-card");
        // The title is HTML-encoded in the markup ("Th&#xEA;m User nhanh").
        html.Should().Contain("Th&#xEA;m User nhanh");
    }

    [Fact]
    public async Task QuickCreate_Page_Offers_The_Ocr_Preview_Endpoint()
    {
        var html = await (await _harness.WebClient.GetAsync("/Users/QuickCreate")).Content.ReadAsStringAsync();

        // The form must point at the MVC proxy (which carries the anti-forgery token),
        // not at the API directly.
        html.Should().Contain("/Users/QuickCreate/OcrPreview");
    }

    // ---------- manual creation ----------

    [Fact]
    public async Task QuickCreate_Creates_A_User_From_Manual_Input()
    {
        var token = await GetTokenAsync("/Users/QuickCreate");

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Mode"] = "manual",
            ["FullName"] = "Nguyễn Tạo Nhanh",
            ["Email"] = $"quick-create-{Guid.NewGuid():N}@example.com",
            ["PhoneNumber"] = "0912345678",
            ["Gender"] = "male"
        });

        var response = await _harness.WebClient.PostAsync("/Users/QuickCreate", form);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _harness.ApiServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleOrDefaultAsync(u => u.FullName == "Nguyễn Tạo Nhanh");

        user.Should().NotBeNull("the quick-create form must create the user through API-01.");
    }

    [Fact]
    public async Task QuickCreate_Does_Not_Create_A_Document_In_Manual_Mode()
    {
        var token = await GetTokenAsync("/Users/QuickCreate");

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Mode"] = "manual",
            ["FullName"] = "Nguyễn Không Giấy Tờ",
            ["Email"] = $"quick-create-nodoc-{Guid.NewGuid():N}@example.com",
            ["PhoneNumber"] = "0912345679"
        });

        var response = await _harness.WebClient.PostAsync("/Users/QuickCreate", form);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _harness.ApiServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleOrDefaultAsync(u => u.FullName == "Nguyễn Không Giấy Tờ");

        user.Should().NotBeNull();
        db.IdentityDocuments.Count(d => d.UserId == user!.Id).Should().Be(0);
    }

    [Fact]
    public async Task QuickCreate_Keeps_The_Form_Data_When_The_Api_Rejects_It()
    {
        var token = await GetTokenAsync("/Users/QuickCreate");
        var email = $"quick-create-dup-{Guid.NewGuid():N}@example.com";

        // First call creates the user.
        using (var first = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Mode"] = "manual",
            ["FullName"] = "Nguyễn Trùng Email",
            ["Email"] = email,
            ["PhoneNumber"] = "0912345680"
        }))
        {
            var ok = await _harness.WebClient.PostAsync("/Users/QuickCreate", first);
            ok.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Second call with the same email must fail and re-render the form with the
        // data still filled in (business rule 6).
        using var second = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Mode"] = "manual",
            ["FullName"] = "Nguyễn Trùng Email",
            ["Email"] = email,
            ["PhoneNumber"] = "0912345681"
        });

        var response = await _harness.WebClient.PostAsync("/Users/QuickCreate", second);
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain(email, "the entered data must survive a failed submission.");

        using var scope = _harness.ApiServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Users.Count(u => u.Email == email).Should().Be(1, "a duplicate user must not be created.");
    }

    [Fact]
    public async Task QuickCreate_Renders_Field_Errors_For_An_Invalid_Email()
    {
        var token = await GetTokenAsync("/Users/QuickCreate");

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Mode"] = "manual",
            ["FullName"] = "Nguyễn Email Sai",
            ["Email"] = "khong-phai-email",
            ["PhoneNumber"] = "0912345682"
        });

        var response = await _harness.WebClient.PostAsync("/Users/QuickCreate", form);
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // The message is HTML-encoded in the markup ("kh&#xF4;ng &#x111;&#xFA;ng ...").
        // Razor wraps the attribute over several lines, so only the leading part is
        // asserted here.
        html.Should().Contain("kh&#xF4;ng &#x111;&#xFA;ng");
        // The input must also be flagged as invalid.
        html.Should().Contain("input-validation-error");
    }

    // ---------- the OCR preview ----------

    [Fact]
    public async Task OcrPreview_Works_Before_The_User_Exists()
    {
        var token = await GetTokenAsync("/Users/QuickCreate");

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "__RequestVerificationToken");
        form.Add(new StringContent("CCCD"), "documentType");

        var front = new ByteArrayContent(CreateJpeg());
        front.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(front, "frontImage", "front.jpg");

        var back = new ByteArrayContent(CreateJpeg());
        back.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(back, "backImage", "back.jpg");

        var response = await _harness.WebClient.PostAsync("/Users/QuickCreate/OcrPreview", form);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"body: {body}");

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("data").GetProperty("fields").EnumerateObject().Should().NotBeEmpty();
    }

    [Fact]
    public async Task OcrPreview_Does_Not_Create_A_User_Or_A_Scan_Record()
    {
        var token = await GetTokenAsync("/Users/QuickCreate");

        using (var scope = _harness.ApiServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var usersBefore = await db.Users.CountAsync();
            var scansBefore = await db.OcrExtractionResults.CountAsync();

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(token), "__RequestVerificationToken");
            form.Add(new StringContent("CCCD"), "documentType");

            var front = new ByteArrayContent(CreateJpeg());
            front.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            form.Add(front, "frontImage", "front.jpg");

            var back = new ByteArrayContent(CreateJpeg());
            back.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            form.Add(back, "backImage", "back.jpg");

            var response = await _harness.WebClient.PostAsync("/Users/QuickCreate/OcrPreview", form);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var db2 = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db2.Users.CountAsync()).Should().Be(usersBefore,
                "a preview must never create a user.");
            (await db2.OcrExtractionResults.CountAsync()).Should().Be(scansBefore,
                "a preview is not a scan: it must not leave a confirmable record behind.");
        }
    }

    [Fact]
    public async Task OcrPreview_Without_The_Back_Image_Returns_An_Error_And_Keeps_The_Manual_Flow()
    {
        var token = await GetTokenAsync("/Users/QuickCreate");

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "__RequestVerificationToken");
        form.Add(new StringContent("CCCD"), "documentType");

        var front = new ByteArrayContent(CreateJpeg());
        front.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(front, "frontImage", "front.jpg");

        var response = await _harness.WebClient.PostAsync("/Users/QuickCreate/OcrPreview", form);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("\"ok\":false");
    }

    // ---------- creating the user together with the document ----------

    [Fact]
    public async Task QuickCreate_In_Ocr_Mode_Creates_The_User_And_The_Document()
    {
        var token = await GetTokenAsync("/Users/QuickCreate");
        var email = $"quick-create-ocr-{Guid.NewGuid():N}@example.com";

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "__RequestVerificationToken");
        form.Add(new StringContent("ocr"), "Mode");
        form.Add(new StringContent("Nguyễn Quét CCCD"), "FullName");
        form.Add(new StringContent(email), "Email");
        form.Add(new StringContent("0912345683"), "PhoneNumber");
        form.Add(new StringContent("CCCD"), "DocumentType");
        form.Add(new StringContent("079095001234"), "DocumentNumber");

        var front = new ByteArrayContent(CreateJpeg());
        front.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(front, "FrontImage", "front.jpg");

        var back = new ByteArrayContent(CreateJpeg());
        back.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(back, "BackImage", "back.jpg");

        var response = await _harness.WebClient.PostAsync("/Users/QuickCreate", form);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"body: {body}");

        using var scope = _harness.ApiServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);

        user.Should().NotBeNull("the user must be created.");
        db.IdentityDocuments.Count(d => d.UserId == user!.Id).Should().Be(1,
            "the identity document must be created together with the user.");
    }

    [Fact]
    public async Task QuickCreate_Keeps_The_User_When_The_Document_Step_Fails()
    {
        var token = await GetTokenAsync("/Users/QuickCreate");
        var email = $"quick-create-partial-{Guid.NewGuid():N}@example.com";

        // The document number is invalid (too short) so the document step fails while
        // the user creation succeeds.
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "__RequestVerificationToken");
        form.Add(new StringContent("ocr"), "Mode");
        form.Add(new StringContent("Nguyễn Tạo Lỗi Giấy Tờ"), "FullName");
        form.Add(new StringContent(email), "Email");
        form.Add(new StringContent("0912345684"), "PhoneNumber");
        form.Add(new StringContent("CCCD"), "DocumentType");
        form.Add(new StringContent("1"), "DocumentNumber");

        var front = new ByteArrayContent(CreateJpeg());
        front.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(front, "FrontImage", "front.jpg");

        var back = new ByteArrayContent(CreateJpeg());
        back.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(back, "BackImage", "back.jpg");

        var response = await _harness.WebClient.PostAsync("/Users/QuickCreate", form);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _harness.ApiServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);

        user.Should().NotBeNull("the user must be kept: the user must not be created twice.");
        db.Users.Count(u => u.Email == email).Should().Be(1);
    }
}
