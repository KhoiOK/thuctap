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
/// Browser-level tests for SCR-03 (tạo User), SCR-04 (sửa User) and SCR-05 (xóa User).
/// They exercise the real forms, the client-side validation, the server-side validation
/// errors and the success toasts.
/// </summary>
[Collection(nameof(BrowserTestCollection))]
public class BrowserUserCrudTests : IAsyncLifetime
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

    // ---------- SCR-03: create ----------

    [Fact]
    public async Task The_Create_Form_Renders_Every_Required_Field()
    {
        await GoToAsync("/Users/Create");

        await Assertions.Expect(_page.Locator("h1.page-title")).ToHaveTextAsync("Thêm người dùng mới");
        await Assertions.Expect(_page.Locator("#FullName")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("#Email")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("#PhoneNumber")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("#DateOfBirth")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("#Gender")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("#Address")).ToBeVisibleAsync();

        // Anti-forgery token must be present on every POST form (the page has the user
        // form plus the global confirm-delete dialog).
        await Assertions.Expect(_page.Locator("input[name='__RequestVerificationToken']").First).ToBeAttachedAsync();
    }

    [Fact]
    public async Task Creating_A_User_Redirects_To_The_Details_Page_And_Shows_A_Success_Toast()
    {
        var email = $"create-{Guid.NewGuid():N}@example.com";

        await GoToAsync("/Users/Create");
        await _page.Locator("#FullName").FillAsync("Nguyễn Tạo Mới");
        await _page.Locator("#Email").FillAsync(email);
        await _page.Locator("#PhoneNumber").FillAsync("0912345678");
        await _page.Locator("#DateOfBirth").FillAsync("1995-04-20");
        await _page.Locator("#Gender").SelectOptionAsync("male");
        await _page.Locator("#Address").FillAsync("1 Đường Nguyễn Huệ");
        await _page.Locator("form button[type='submit']").First.ClickAsync();

        await _page.WaitForURLAsync(url => url.Contains("/Users/Details/"), new PageWaitForURLOptions { Timeout = 15000 });
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(_page.Locator("h1.font-display")).ToHaveTextAsync("Nguyễn Tạo Mới");
        await Assertions.Expect(_page.Locator("#toast-container .toast-success")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("#toast-container")).ToContainTextAsync("Đã thêm người dùng");

        // The user must really exist in the database.
        using var scope = _harness.ApiServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
        saved.Should().NotBeNull();
        saved!.FullName.Should().Be("Nguyễn Tạo Mới");
        saved.DateOfBirth.Should().Be(new DateOnly(1995, 4, 20));
    }

    [Fact]
    public async Task The_Create_Form_Rejects_An_Invalid_Email_Client_Side()
    {
        await GoToAsync("/Users/Create");
        await _page.Locator("#FullName").FillAsync("Nguyễn Email Sai");
        await _page.Locator("#Email").FillAsync("khong-phai-email");
        await _page.Locator("#PhoneNumber").FillAsync("0912345679");
        await _page.Locator("form button[type='submit']").First.ClickAsync();

        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The browser blocks the submission and the unobtrusive validator marks the
        // field as invalid with a readable message.
        _page.Url.Should().Contain("/Users/Create");
        await Assertions.Expect(_page.Locator("#Email")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("field-invalid"));
        await Assertions.Expect(_page.Locator("[data-valmsg-for='Email']")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("[data-valmsg-for='Email']")).ToContainTextAsync("không đúng định dạng");
    }

    [Fact]
    public async Task The_Create_Form_Shows_A_Field_Error_For_A_Duplicate_Email()
    {
        var email = $"dup-{Guid.NewGuid():N}@example.com";
        await SeedUserAsync("Người Dùng Gốc", email, "0912345680");

        await GoToAsync("/Users/Create");
        await _page.Locator("#FullName").FillAsync("Người Dùng Trùng");
        await _page.Locator("#Email").FillAsync(email);
        await _page.Locator("#PhoneNumber").FillAsync("0912345681");
        await _page.Locator("form button[type='submit']").First.ClickAsync();

        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The server rejects the duplicate and the error is rendered on the email field.
        _page.Url.Should().Contain("/Users/Create");
        await Assertions.Expect(_page.Locator("#email-error")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_Create_Form_Blocks_Submission_When_A_Required_Field_Is_Empty()
    {
        await GoToAsync("/Users/Create");
        await _page.Locator("#FullName").FillAsync("");
        await _page.Locator("#Email").FillAsync("");
        await _page.Locator("#PhoneNumber").FillAsync("");
        await _page.Locator("form button[type='submit']").First.ClickAsync();

        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The form must not be submitted, and the required fields must be flagged.
        _page.Url.Should().Contain("/Users/Create");
        await Assertions.Expect(_page.Locator("span.field-validation-error").First).ToBeVisibleAsync();
    }

    // ---------- SCR-04: edit ----------

    [Fact]
    public async Task The_Edit_Form_Is_Pre_Filled_With_The_Current_Data()
    {
        var id = await SeedUserAsync("Nguyễn Sửa", "edit-me@example.com", "0912345682");

        await GoToAsync($"/Users/Edit/{id}");

        await Assertions.Expect(_page.Locator("#FullName")).ToHaveValueAsync("Nguyễn Sửa");
        await Assertions.Expect(_page.Locator("#Email")).ToHaveValueAsync("edit-me@example.com");
        await Assertions.Expect(_page.Locator("#PhoneNumber")).ToHaveValueAsync("0912345682");
    }

    [Fact]
    public async Task Editing_A_User_Saves_The_Changes_And_Shows_A_Success_Toast()
    {
        var id = await SeedUserAsync("Tên Cũ", "edit-save@example.com", "0912345683");

        await GoToAsync($"/Users/Edit/{id}");
        await _page.Locator("#FullName").FillAsync("Tên Mới");
        await _page.Locator("#Address").FillAsync("99 Nguyễn Huệ, TP.HCM");
        await _page.Locator("form button[type='submit']").First.ClickAsync();

        await _page.WaitForURLAsync(url => url.Contains("/Users/Details/"), new PageWaitForURLOptions { Timeout = 15000 });
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(_page.Locator("h1.font-display")).ToHaveTextAsync("Tên Mới");
        await Assertions.Expect(_page.Locator("#toast-container .toast-success")).ToBeVisibleAsync();

        using var scope = _harness.ApiServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await db.Users.SingleAsync(u => u.Id == id);
        saved.FullName.Should().Be("Tên Mới");
        saved.Address.Should().Be("99 Nguyễn Huệ, TP.HCM");
    }

    // ---------- SCR-05: delete ----------

    [Fact]
    public async Task Deleting_A_User_Shows_The_Confirmation_Modal_First()
    {
        var id = await SeedUserAsync("Người Sắp Xóa", "delete-me@example.com", "0912345684");

        await GoToAsync($"/Users/Details/{id}");

        // The delete button must open a confirmation dialog, not delete immediately.
        await _page.GetByRole(AriaRole.Link, new() { Name = "Xóa", Exact = true }).ClickAsync();

        var modal = _page.Locator("#confirmDeleteModal");
        await Assertions.Expect(modal).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-open"));
        await Assertions.Expect(modal).ToContainTextAsync("Bạn có chắc chắn");

        // Nothing was deleted yet.
        using (var scope = _harness.ApiServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Users.AnyAsync(u => u.Id == id)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task The_Delete_Modal_Can_Be_Cancelled()
    {
        var id = await SeedUserAsync("Người Giữ Nguyên", "keep-me@example.com", "0912345685");

        await GoToAsync($"/Users/Details/{id}");
        await _page.GetByRole(AriaRole.Link, new() { Name = "Xóa", Exact = true }).ClickAsync();

        var modal = _page.Locator("#confirmDeleteModal");
        await Assertions.Expect(modal).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-open"));
        await _page.Locator("#confirmDeleteModal [data-modal-close]").First.ClickAsync();
        await Assertions.Expect(modal).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-open"));

        using var scope = _harness.ApiServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Users.AnyAsync(u => u.Id == id)).Should().BeTrue();
    }

    [Fact]
    public async Task Confirming_The_Delete_Soft_Deletes_The_User_And_Shows_A_Success_Toast()
    {
        var id = await SeedUserAsync("Người Bị Xóa", "delete-confirm@example.com", "0912345686");

        await GoToAsync($"/Users/Details/{id}");
        await _page.GetByRole(AriaRole.Link, new() { Name = "Xóa", Exact = true }).ClickAsync();

        var modal = _page.Locator("#confirmDeleteModal");
        await Assertions.Expect(modal).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-open"));
        await _page.Locator("#confirmDeleteModal [data-submit]").ClickAsync();

        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Assertions.Expect(_page.Locator("#toast-container .toast-success")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("#toast-container")).ToContainTextAsync("đã được xóa thành công");

        // Soft delete: the row still exists but is flagged as deleted.
        using var scope = _harness.ApiServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var deleted = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == id);
        deleted.IsDeleted.Should().BeTrue();
        deleted.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_Deleted_User_Disappears_From_The_List_And_Its_Details_Page()
    {
        var id = await SeedUserAsync("Người Bị Ẩn", "hidden@example.com", "0912345687");

        await GoToAsync($"/Users/Details/{id}");
        await _page.GetByRole(AriaRole.Link, new() { Name = "Xóa", Exact = true }).ClickAsync();
        await _page.Locator("#confirmDeleteModal [data-submit]").ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await GoToAsync("/Users");
        await Assertions.Expect(_page.GetByRole(AriaRole.Link, new() { Name = "Người Bị Ẩn", Exact = true }))
            .Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_Deleted_User_Can_Be_Re_Created_With_The_Same_Email()
    {
        var email = $"reuse-{Guid.NewGuid():N}@example.com";
        var id = await SeedUserAsync("Người Đã Xóa", email, "0912345688");

        await GoToAsync($"/Users/Details/{id}");
        await _page.GetByRole(AriaRole.Link, new() { Name = "Xóa", Exact = true }).ClickAsync();
        await _page.Locator("#confirmDeleteModal [data-submit]").ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The same email must be reusable after a soft delete (FR-06).
        await GoToAsync("/Users/Create");
        await _page.Locator("#FullName").FillAsync("Người Dùng Mới Cùng Email");
        await _page.Locator("#Email").FillAsync(email);
        await _page.Locator("#PhoneNumber").FillAsync("0912345689");
        await _page.Locator("form button[type='submit']").First.ClickAsync();

        await _page.WaitForURLAsync(url => url.Contains("/Users/Details/"), new PageWaitForURLOptions { Timeout = 15000 });
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Assertions.Expect(_page.Locator("h1.font-display")).ToHaveTextAsync("Người Dùng Mới Cùng Email");
    }
}
