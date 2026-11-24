using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using UserManagement.Api.Data;
using UserManagement.Api.Entities;
using UserManagement.Api.FileStorage;
using UserManagement.Web.Tests.Infrastructure;
using Xunit;

namespace UserManagement.Web.Tests;

/// <summary>
/// Browser-level tests for SCR-06 (thêm/sửa/xóa giấy tờ), SCR-07 (quét OCR trong
/// form giấy tờ), API-10 (image proxy) và lightbox. They drive a REAL Chromium
/// browser against REAL Kestrel hosts with <see cref="FakePaddleOcrClient"/> as the
/// OCR engine and <see cref="TestCloudinaryStorage"/> as the image store.
/// </summary>
[Collection(nameof(BrowserTestCollection))]
public class BrowserDocumentTests : IAsyncLifetime
{
    private readonly BrowserTestHarness _harness = new();
    private IBrowserContext _context = null!;
    private IPage _page = null!;

    private readonly List<string> _consoleErrors = new();
    private readonly List<string> _httpErrors = new();

    public async Task InitializeAsync()
    {
        await _harness.InitializeAsync();
        (_context, _page) = await _harness.NewPageAsync();

        _page.Console += (_, msg) =>
        {
            if (msg.Type == "error") _consoleErrors.Add(msg.Text);
        };
        _page.Response += (_, res) =>
        {
            if (res.Status >= 400 && !res.Url.Contains("fonts.g"))
                _httpErrors.Add($"{res.Status} {res.Url}");
        };
    }

    public async Task DisposeAsync()
    {
        await _context.CloseAsync();
        await _harness.DisposeAsync();
    }

    private Task GoToAsync(string path)
        => _page.GotoAsync(_harness.Url(path), new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

    private async Task<Guid> SeedUserAsync(string fullName, string email, string phone)
    {
        using var scope = _harness.ApiServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = new User
        {
            FullName = fullName,
            FullNameSearch = User.BuildFullNameSearch(fullName),
            Email = email.ToLowerInvariant(),
            PhoneNumber = phone,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Guid> SeedDocumentAsync(Guid userId, DocumentType type, string number)
    {
        using var scope = _harness.ApiServices.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<ICloudinaryStorage>();
        using var img = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var front = await storage.UploadAsync("test", "front", img, "image/jpeg", CancellationToken.None);
        var back = await storage.UploadAsync("test", "back", new MemoryStream(new byte[] { 5, 6, 7, 8 }), "image/jpeg", CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var doc = new IdentityDocument
        {
            UserId = userId,
            DocumentType = type,
            DocumentNumber = number,
            FrontImagePublicId = front.PublicId,
            BackImagePublicId = type == DocumentType.Passport ? null : back.PublicId
        };
        db.IdentityDocuments.Add(doc);
        await db.SaveChangesAsync();
        return doc.Id;
    }

    private string NewImage(string name)
        => TestImages.WriteJpeg(Path.Combine(Path.GetTempPath(), $"doc-{Guid.NewGuid():N}"), name);

    // ---------- create ----------

    [Fact]
    public async Task The_Document_Form_Renders_With_Scan_And_Passport_Switch()
    {
        var id = await SeedUserAsync("Nguyễn Giấy Tờ", $"doctest-{Guid.NewGuid():N}@example.com", "0912345691");

        await GoToAsync($"/Users/{id}/Documents/Create");

        await Assertions.Expect(_page.Locator("h1.page-title")).ToHaveTextAsync("Thêm giấy tờ");
        await Assertions.Expect(_page.Locator("#DocumentNumber")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("[data-ocr-scan]")).ToBeVisibleAsync();
        // The scan button stays disabled until both images are chosen.
        await Assertions.Expect(_page.Locator("[data-ocr-scan]")).ToBeDisabledAsync();

        // Passport hides the back-image section (BR-07).
        await _page.Locator("select[data-doc-type]").SelectOptionAsync("Passport");
        await Assertions.Expect(_page.Locator("[data-image-section='back']")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Creating_A_Document_With_Images_Shows_It_On_The_Details_Page()
    {
        var userId = await SeedUserAsync("Nguyễn Thêm Giấy", $"adddoc-{Guid.NewGuid():N}@example.com", "0912345692");
        var number = $"079095{new Random().Next(100000, 999999)}";

        await GoToAsync($"/Users/{userId}/Documents/Create");
        await _page.Locator("#DocumentNumber").FillAsync(number);
        await _page.Locator("input[name='FrontImage']").SetInputFilesAsync(NewImage("front.jpg"));
        await _page.Locator("input[name='BackImage']").SetInputFilesAsync(NewImage("back.jpg"));
        await _page.Locator("form button[type='submit']").First.ClickAsync();

        await _page.WaitForURLAsync(url => url.Contains("/Users/Details/"), new PageWaitForURLOptions { Timeout = 20000 });
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(_page.Locator("#toast-container .toast-success")).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByText(number)).ToBeVisibleAsync();

        using var scope = _harness.ApiServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await db.IdentityDocuments.SingleOrDefaultAsync(d => d.UserId == userId && d.DocumentNumber == number);
        saved.Should().NotBeNull();
        saved!.FrontImagePublicId.Should().NotBeNullOrEmpty();

        AssertNoBrowserErrors();
    }

    [Fact]
    public async Task The_Document_Form_Blocks_An_Empty_Document_Number_Client_Side()
    {
        var userId = await SeedUserAsync("Nguyễn Thiếu Số", $"nodocnum-{Guid.NewGuid():N}@example.com", "0912345693");

        await GoToAsync($"/Users/{userId}/Documents/Create");
        await _page.Locator("form button[type='submit']").First.ClickAsync();
        await _page.WaitForTimeoutAsync(1200);

        _page.Url.Should().Contain("/Documents/Create");
        await Assertions.Expect(_page.Locator("#DocumentNumber.input-validation-error")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Uploading_A_Non_Image_File_Shows_A_Server_Error()
    {
        var userId = await SeedUserAsync("Nguyễn Ảnh Lỗi", $"badimg-{Guid.NewGuid():N}@example.com", "0912345694");

        await GoToAsync($"/Users/{userId}/Documents/Create");
        await _page.Locator("#DocumentNumber").FillAsync("079095001237");
        var bad = TestImages.WriteInvalidImage(Path.Combine(Path.GetTempPath(), $"bad-{Guid.NewGuid():N}"), "front.jpg");
        await _page.Locator("input[name='FrontImage']").SetInputFilesAsync(bad);
        await _page.Locator("input[name='BackImage']").SetInputFilesAsync(NewImage("back.jpg"));
        await _page.Locator("form button[type='submit']").First.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The server rejects the fake image and the form re-renders with an error.
        _page.Url.Should().Contain("/Documents/Create");
        await Assertions.Expect(_page.Locator(".alert-danger")).ToBeVisibleAsync();
    }

    // ---------- OCR scan ----------

    [Fact]
    public async Task Ocr_Scan_Fills_The_Document_Number_And_Submits_Through_Confirm()
    {
        var userId = await SeedUserAsync("Nguyễn Quét Giấy", $"scandoc-{Guid.NewGuid():N}@example.com", "0912345695");

        await GoToAsync($"/Users/{userId}/Documents/Create");
        await _page.Locator("input[name='FrontImage']").SetInputFilesAsync(NewImage("front.jpg"));
        await _page.Locator("input[name='BackImage']").SetInputFilesAsync(NewImage("back.jpg"));

        await Assertions.Expect(_page.Locator("[data-ocr-scan]")).ToBeEnabledAsync();
        await _page.Locator("[data-ocr-scan]").ClickAsync();

        // The scan panel renders and the document number is auto-filled from the fake OCR.
        await Assertions.Expect(_page.Locator("[data-ocr-panel]")).ToBeVisibleAsync(new() { Timeout = 20000 });
        await Assertions.Expect(_page.Locator("#DocumentNumber")).ToHaveValueAsync("079095001234", new() { Timeout = 15000 });

        await _page.Locator("form button[type='submit']").First.ClickAsync();
        await _page.WaitForURLAsync(url => url.Contains("/Users/Details/"), new PageWaitForURLOptions { Timeout = 20000 });
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(_page.Locator("#toast-container .toast-success")).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByText("079095001234")).ToBeVisibleAsync();

        AssertNoBrowserErrors();
    }

    // ---------- edit ----------

    [Fact]
    public async Task The_Edit_Form_Is_Pre_Filled_And_Saves_Changes()
    {
        var userId = await SeedUserAsync("Nguyễn Sửa Giấy", $"editdoc-{Guid.NewGuid():N}@example.com", "0912345696");
        var docId = await SeedDocumentAsync(userId, DocumentType.CCCD, "079095001234");

        await GoToAsync($"/Users/{userId}/Documents/Edit/{docId}");

        await Assertions.Expect(_page.Locator("h1.page-title")).ToHaveTextAsync("Chỉnh sửa giấy tờ");
        await Assertions.Expect(_page.Locator("#DocumentNumber")).ToHaveValueAsync("079095001234");

        await _page.Locator("#DocumentNumber").FillAsync("079095009999");
        await _page.Locator("form button[type='submit']").First.ClickAsync();

        await _page.WaitForURLAsync(url => url.Contains("/Users/Details/"), new PageWaitForURLOptions { Timeout = 15000 });
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(_page.Locator("#toast-container .toast-success")).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByText("079095009999")).ToBeVisibleAsync();

        AssertNoBrowserErrors();
    }

    // ---------- image proxy + lightbox ----------

    [Fact]
    public async Task The_Front_Image_Proxy_Returns_The_Stored_Image()
    {
        var userId = await SeedUserAsync("Nguyễn Xem Ảnh", $"imgdoc-{Guid.NewGuid():N}@example.com", "0912345697");
        var docId = await SeedDocumentAsync(userId, DocumentType.CCCD, "079095001235");

        using var http = new HttpClient { BaseAddress = _harness.WebBaseAddress };
        var response = await http.GetAsync($"/Users/{userId}/Documents/{docId}/Image/front");

        response.IsSuccessStatusCode.Should().BeTrue($"the image proxy must serve the stored image, got {(int)response.StatusCode}");
        response.Content.Headers.ContentType!.MediaType.Should().StartWith("image/");
    }

    [Fact]
    public async Task Clicking_The_Front_Image_Button_Opens_The_Lightbox()
    {
        var userId = await SeedUserAsync("Nguyễn Lightbox", $"lightbox-{Guid.NewGuid():N}@example.com", "0912345698");
        await SeedDocumentAsync(userId, DocumentType.CCCD, "079095001236");

        await GoToAsync($"/Users/Details/{userId}");

        await _page.GetByRole(AriaRole.Button, new() { Name = "Mặt trước" }).First.ClickAsync();
        await Assertions.Expect(_page.Locator(".lightbox-overlay")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator(".lightbox-overlay img")).ToBeVisibleAsync();

        await _page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(_page.Locator(".lightbox-overlay")).ToBeHiddenAsync();

        AssertNoBrowserErrors();
    }

    // ---------- delete ----------

    [Fact]
    public async Task Deleting_A_Document_Asks_For_Confirmation_Then_Removes_The_Card()
    {
        var userId = await SeedUserAsync("Nguyễn Xóa Giấy", $"deldoc-{Guid.NewGuid():N}@example.com", "0912345699");
        await SeedDocumentAsync(userId, DocumentType.CCCD, "079095001237");

        await GoToAsync($"/Users/Details/{userId}");
        await Assertions.Expect(_page.GetByText("079095001237")).ToBeVisibleAsync();

        await _page.GetByRole(AriaRole.Link, new() { Name = "Xóa CCCD" }).First.ClickAsync();

        var modal = _page.Locator("#confirmDeleteModal");
        await Assertions.Expect(modal).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-open"));
        await Assertions.Expect(modal).ToContainTextAsync("Xóa giấy tờ?");

        await _page.Locator("#confirmDeleteModal [data-submit]").ClickAsync();
        await Assertions.Expect(_page.Locator("#toast-container .toast-success")).ToBeVisibleAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(_page.GetByText("079095001237")).Not.ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByText("Chưa có giấy tờ tùy thân")).ToBeVisibleAsync();

        using var scope = _harness.ApiServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.IdentityDocuments.CountAsync(d => d.UserId == userId)).Should().Be(0);
    }

    // ---------- helper ----------

    private void AssertNoBrowserErrors()
    {
        _consoleErrors.Should().BeEmpty(
            $"the page must not log console errors. HTTP errors: {string.Join(" | ", _httpErrors)}");
        _httpErrors.Should().BeEmpty("no request may return an error status");
    }
}
