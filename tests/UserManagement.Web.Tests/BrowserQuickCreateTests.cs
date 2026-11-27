using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using UserManagement.Api.Data;
using UserManagement.Api.Entities;
using UserManagement.Web.Tests.Infrastructure;
using Xunit;

namespace UserManagement.Web.Tests;

/// <summary>
/// Browser-level tests for SCR-03b (Thêm User nhanh): manual mode and the CCCD OCR
/// preview branch. They drive a REAL Chromium browser against REAL Kestrel hosts,
/// so the mode switch, the image preview, the OCR panel and the final submit are
/// all exercised end-to-end (FakePaddleOcrClient stands in for the OCR service).
/// </summary>
[Collection(nameof(BrowserTestCollection))]
public class BrowserQuickCreateTests : IAsyncLifetime
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

    private string NewImage(string name, byte r = 200, byte g = 30, byte b = 30)
        => TestImages.WriteJpeg(Path.Combine(Path.GetTempPath(), $"qc-{Guid.NewGuid():N}"), name, r: r, g: g, b: b);

    // ---------- page rendering ----------

    [Fact]
    public async Task QuickCreate_Page_Renders_Both_Input_Modes()
    {
        await GoToAsync("/Users/QuickCreate");

        await Assertions.Expect(_page.Locator("h1.page-title")).ToHaveTextAsync("Thêm User nhanh");
        await Assertions.Expect(_page.Locator("[data-mode-option='manual']")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("[data-mode-option='ocr']")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("[data-quick-submit]")).ToBeVisibleAsync();

        // Manual mode is active by default: the OCR branch stays hidden.
        await Assertions.Expect(_page.Locator("[data-mode-option='manual']")).ToHaveAttributeAsync("aria-checked", "true");
    }

    [Fact]
    public async Task Switching_To_Ocr_Mode_Reveals_The_Image_Branch()
    {
        await GoToAsync("/Users/QuickCreate");

        await _page.Locator("[data-mode-option='ocr']").ClickAsync();

        await Assertions.Expect(_page.Locator("[data-mode-option='ocr']")).ToHaveAttributeAsync("aria-checked", "true");
        await Assertions.Expect(_page.Locator("input[data-ocr-image='front']")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("input[data-ocr-image='back']")).ToBeVisibleAsync();
        // The preview button stays disabled until both images are chosen.
        await Assertions.Expect(_page.Locator("[data-ocr-preview-btn]")).ToBeDisabledAsync();
    }

    // ---------- manual creation ----------

    [Fact]
    public async Task QuickCreate_Manual_Creates_A_User_Without_A_Document()
    {
        var email = $"qc-{Guid.NewGuid():N}@example.com";

        await GoToAsync("/Users/QuickCreate");
        await _page.Locator("#FullName").FillAsync("Nguyễn Tạo Nhanh");
        await _page.Locator("#Email").FillAsync(email);
        await _page.Locator("#PhoneNumber").FillAsync("0912345678");
        await _page.Locator("[data-quick-submit]").ClickAsync();

        await _page.WaitForURLAsync(url => url.Contains("/Users/Details/"), new PageWaitForURLOptions { Timeout = 15000 });
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(_page.Locator("h1.font-display")).ToHaveTextAsync("Nguyễn Tạo Nhanh");
        await Assertions.Expect(_page.Locator("#toast-container .toast-success")).ToBeVisibleAsync();

        using var scope = _harness.ApiServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await db.Users.Include(u => u.IdentityDocuments).SingleOrDefaultAsync(u => u.Email == email);
        saved.Should().NotBeNull();
        saved!.IdentityDocuments.Should().BeEmpty("manual mode must not create a document.");
    }

    [Fact]
    public async Task QuickCreate_Blocks_Empty_Required_Fields_Client_Side()
    {
        await GoToAsync("/Users/QuickCreate");
        await _page.Locator("[data-quick-submit]").ClickAsync();
        await _page.WaitForTimeoutAsync(1200);

        // The browser blocks the submission: we stay on the form and the fields are flagged.
        _page.Url.Should().Contain("/Users/QuickCreate");
        await Assertions.Expect(_page.Locator("#Email.input-validation-error")).ToBeVisibleAsync();
    }

    // ---------- OCR branch ----------

    [Fact]
    public async Task Ocr_Preview_Prefills_The_Form_Without_Creating_A_User()
    {
        await GoToAsync("/Users/QuickCreate");
        await _page.Locator("[data-mode-option='ocr']").ClickAsync();

        var front = NewImage("front.jpg");
        var back = NewImage("back.jpg", r: 20, g: 120, b: 200);
        await _page.Locator("input[data-ocr-image='front']").SetInputFilesAsync(front);
        await _page.Locator("input[data-ocr-image='back']").SetInputFilesAsync(back);

        await Assertions.Expect(_page.Locator("[data-ocr-preview-btn]")).ToBeEnabledAsync();
        await _page.Locator("[data-ocr-preview-btn]").ClickAsync();

        // The OCR panel renders and the high-confidence fields are auto-filled.
        await Assertions.Expect(_page.Locator("[data-ocr-panel]")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Assertions.Expect(_page.Locator("#FullName")).Not.ToBeEmptyAsync(new() { Timeout = 15000 });

        // A preview must not persist anything.
        using var scope = _harness.ApiServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Users.CountAsync()).Should().Be(0, "the OCR preview must not create a user.");
    }

    /// <summary>
    /// Regression test: the OCR field is named "issueDate" (docs/OCR-Cloudinary-Spec.md
    /// §3.3) while the form field is named "IssuedDate". When the two got mixed up, the
    /// "Ngày cấp" input stayed empty even though the API had read the date.
    /// </summary>
    [Fact]
    public async Task Ocr_Preview_Fills_The_Issued_Date()
    {
        await GoToAsync("/Users/QuickCreate");
        await _page.Locator("[data-mode-option='ocr']").ClickAsync();

        var front = NewImage("front.jpg");
        var back = NewImage("back.jpg", r: 20, g: 120, b: 200);
        await _page.Locator("input[data-ocr-image='front']").SetInputFilesAsync(front);
        await _page.Locator("input[data-ocr-image='back']").SetInputFilesAsync(back);
        await _page.Locator("[data-ocr-preview-btn]").ClickAsync();

        await Assertions.Expect(_page.Locator("[data-ocr-panel]")).ToBeVisibleAsync(new() { Timeout = 15000 });

        // The fake engine reports the issue date as "10/05/2021" (issueDate).
        await Assertions.Expect(_page.Locator("#IssuedDate")).ToHaveValueAsync("2021-05-10", new() { Timeout = 15000 });
        await Assertions.Expect(_page.Locator("#ExpiryDate")).ToHaveValueAsync("2036-04-20", new() { Timeout = 15000 });
    }

    [Fact]
    public async Task QuickCreate_Ocr_Creates_The_User_Together_With_The_Document()
    {
        var email = $"qc-ocr-{Guid.NewGuid():N}@example.com";

        await GoToAsync("/Users/QuickCreate");
        await _page.Locator("[data-mode-option='ocr']").ClickAsync();

        var front = NewImage("front.jpg");
        var back = NewImage("back.jpg", r: 20, g: 120, b: 200);
        await _page.Locator("input[data-ocr-image='front']").SetInputFilesAsync(front);
        await _page.Locator("input[data-ocr-image='back']").SetInputFilesAsync(back);
        await _page.Locator("[data-ocr-preview-btn]").ClickAsync();
        await Assertions.Expect(_page.Locator("[data-ocr-panel]")).ToBeVisibleAsync(new() { Timeout = 15000 });

        // Email/phone are not part of the CCCD scan: the user types them manually.
        await _page.Locator("#Email").FillAsync(email);
        await _page.Locator("#PhoneNumber").FillAsync("0912345683");
        // The OCR pre-fills the document number; make sure the field holds it.
        var docNumber = await _page.Locator("#DocumentNumber").InputValueAsync();
        if (string.IsNullOrWhiteSpace(docNumber))
            await _page.Locator("#DocumentNumber").FillAsync("079095001234");

        await _page.Locator("[data-quick-submit]").ClickAsync();
        await _page.WaitForURLAsync(url => url.Contains("/Users/Details/"), new PageWaitForURLOptions { Timeout = 20000 });
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(_page.Locator("#toast-container .toast-success")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("#toast-container")).ToContainTextAsync("kèm giấy tờ");

        using var scope = _harness.ApiServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await db.Users.Include(u => u.IdentityDocuments).SingleOrDefaultAsync(u => u.Email == email);
        saved.Should().NotBeNull();
        saved!.IdentityDocuments.Should().HaveCount(1, "the OCR branch must create the document together with the user.");
    }

    [Fact]
    public async Task Passport_Does_Not_Require_The_Back_Image()
    {
        await GoToAsync("/Users/QuickCreate");
        await _page.Locator("[data-mode-option='ocr']").ClickAsync();

        // Passport only needs the front image.
        await _page.Locator("select[data-doc-type]").SelectOptionAsync("Passport");
        await Assertions.Expect(_page.Locator("[data-ocr-back-section]")).ToBeHiddenAsync();

        var front = NewImage("front.jpg");
        await _page.Locator("input[data-ocr-image='front']").SetInputFilesAsync(front);

        await Assertions.Expect(_page.Locator("[data-ocr-preview-btn]")).ToBeEnabledAsync();
    }

    // ---------- the document step fails after the user was created ----------

    /// <summary>
    /// Regression test for the bug the user reported: the Quick Create OCR branch
    /// created the user, the document save failed (the image-storage backend was
    /// not configured, so the API answered HTTP 500), and the flow dropped the user
    /// on the details page where they had to hunt for "Thêm giấy tờ" and open
    /// /Users/{id}/Documents/Create from scratch.
    ///
    /// The user must be created exactly ONCE and the flow must continue on the
    /// document form, pre-filled with the values that were already entered.
    /// </summary>
    [Fact]
    public async Task QuickCreate_Ocr_When_The_Document_Fails_Continues_On_The_Document_Form()
    {
        // A document number that is already taken makes the API reject the document
        // step with HTTP 409 - the same code path a broken storage backend produces.
        const string takenDocumentNumber = "079095001234";

        var blockerEmail = $"qc-blocker-{Guid.NewGuid():N}@example.com";
        Guid blockerUserId;

        using (var scope = _harness.ApiServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var blocker = new User
            {
                Id = Guid.NewGuid(),
                FullName = "Người Dùng Chặn",
                Email = blockerEmail,
                PhoneNumber = "0900000001",
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(blocker);
            db.IdentityDocuments.Add(new IdentityDocument
            {
                Id = Guid.NewGuid(),
                UserId = blocker.Id,
                DocumentType = DocumentType.CCCD,
                DocumentNumber = takenDocumentNumber,
                FrontImagePublicId = "test/front.jpg",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            blockerUserId = blocker.Id;
        }

        var email = $"qc-ocr-fail-{Guid.NewGuid():N}@example.com";

        await GoToAsync("/Users/QuickCreate");
        await _page.Locator("[data-mode-option='ocr']").ClickAsync();

        var front = NewImage("front.jpg");
        var back = NewImage("back.jpg", r: 20, g: 120, b: 200);
        await _page.Locator("input[data-ocr-image='front']").SetInputFilesAsync(front);
        await _page.Locator("input[data-ocr-image='back']").SetInputFilesAsync(back);
        await _page.Locator("[data-ocr-preview-btn]").ClickAsync();
        await Assertions.Expect(_page.Locator("[data-ocr-panel]")).ToBeVisibleAsync(new() { Timeout = 15000 });

        await _page.Locator("#Email").FillAsync(email);
        await _page.Locator("#PhoneNumber").FillAsync("0912345699");
        // Use the number that is already taken -> the document step must fail.
        await _page.Locator("#DocumentNumber").FillAsync(takenDocumentNumber);

        await _page.Locator("[data-quick-submit]").ClickAsync();

        // The flow must land on the document form, NOT on the details page.
        await _page.WaitForURLAsync(
            url => url.Contains("/Documents/Create"),
            new PageWaitForURLOptions { Timeout = 20000 });
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The user was created exactly once - the document failure must not create it again.
        using (var scope = _harness.ApiServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var created = await db.Users.Include(u => u.IdentityDocuments)
                .Where(u => u.Email == email)
                .ToListAsync();
            created.Should().HaveCount(1, "the user must never be created twice.");
            created[0].IdentityDocuments.Should().BeEmpty("the document step failed.");
            created[0].Id.Should().NotBe(blockerUserId);
        }

        // The message must say the user was created but the document was not saved.
        await Assertions.Expect(_page.Locator(".alert-danger")).ToContainTextAsync(
            "Người dùng đã được tạo nhưng giấy tờ chưa được lưu");

        // The values the user already typed are carried over - nothing to retype.
        await Assertions.Expect(_page.Locator("#DocumentNumber")).ToHaveValueAsync(takenDocumentNumber);
    }

    /// <summary>
    /// The document form must accept the pre-fill query parameters so the failed
    /// Quick Create can hand over the values the user already entered.
    /// </summary>
    [Fact]
    public async Task Document_Create_Form_Prefills_The_Values_From_The_Query_String()
    {
        var email = $"qc-prefill-{Guid.NewGuid():N}@example.com";

        Guid userId;
        using (var scope = _harness.ApiServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = new User
            {
                Id = Guid.NewGuid(),
                FullName = "Người Dùng Prefill",
                Email = email,
                PhoneNumber = "0900000002",
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        await GoToAsync(
            $"/Users/{userId}/Documents/Create"
            + "?documentType=CCCD&documentNumber=001234567890&issuedDate=2021-05-10&expiryDate=2036-04-20");

        await Assertions.Expect(_page.Locator("#DocumentNumber")).ToHaveValueAsync("001234567890");
        await Assertions.Expect(_page.Locator("#IssuedDate")).ToHaveValueAsync("2021-05-10");
        await Assertions.Expect(_page.Locator("#ExpiryDate")).ToHaveValueAsync("2036-04-20");
        await Assertions.Expect(_page.Locator("select#DocumentType")).ToHaveValueAsync("CCCD");
    }
}
