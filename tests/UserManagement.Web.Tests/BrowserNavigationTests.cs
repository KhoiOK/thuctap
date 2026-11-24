using System.Text.RegularExpressions;
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
/// Browser-level tests for SCR-01 (danh sách User), SCR-02 (chi tiết) and the
/// navigation between the screens. These drive a REAL Chromium browser against a
/// REAL Kestrel-hosted MVC app, so they cover the rendering, the responsive layout,
/// the console output and the failed network requests - things an HTTP-level test
/// cannot see.
/// </summary>
[Collection(nameof(BrowserTestCollection))]
public class BrowserNavigationTests : IAsyncLifetime
{
    private readonly BrowserTestHarness _harness = new();
    private IBrowserContext _context = null!;
    private IPage _page = null!;

    private readonly List<string> _consoleErrors = new();
    private readonly List<string> _failedRequests = new();
    private readonly List<string> _httpErrors = new();

    public async Task InitializeAsync()
    {
        await _harness.InitializeAsync();
        (_context, _page) = await _harness.NewPageAsync();

        // Collect console errors and failed requests for every test.
        _page.Console += (_, msg) =>
        {
            // The message alone does not say WHICH resource failed, so the URL is
            // taken from the matching response event (collected in _httpErrors).
            if (msg.Type == "error") _consoleErrors.Add(msg.Text);
        };
        _page.RequestFailed += (_, req) =>
        {
            // Google Fonts is blocked in the sandbox; that is a third-party asset,
            // not an application request, so it is not counted as a failure.
            if (!req.Url.Contains("fonts.googleapis.com") && !req.Url.Contains("fonts.gstatic.com"))
                _failedRequests.Add($"{req.Method} {req.Url} :: {req.Failure}");
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

    private async Task SeedUserAsync(string fullName, string email, string phone, string? address = null)
    {
        using var scope = _harness.ApiServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Users.Add(new User
        {
            FullName = fullName,
            // The search column is computed by UserService on create; a directly seeded
            // row must compute it too, otherwise name search cannot find the user.
            FullNameSearch = User.BuildFullNameSearch(fullName),
            Email = email.ToLowerInvariant(),
            PhoneNumber = phone,
            Address = address,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private Task GoAsync(IPage page, string url)
        => page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

    /// <summary>Navigates to a path of the Web app (absolute URL is built for you).</summary>
    private Task GoToAsync(string path)
        => GoAsync(_page, _harness.Url(path));

    // ---------- navigation ----------

    [Fact]
    public async Task Root_Redirects_To_The_User_List()
    {
        var response = await _page.GotoAsync(_harness.Url("/"), new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        response!.Url.Should().Contain("/Users");
        _page.Url.Should().Contain("/Users");
    }

    [Fact]
    public async Task User_List_Renders_The_Table_And_Statistics()
    {
        await SeedUserAsync("Nguyễn Văn A", $"a-{Guid.NewGuid():N}@example.com", "0912345671");
        await SeedUserAsync("Trần Thị B", $"b-{Guid.NewGuid():N}@example.com", "0912345672");

        await GoToAsync("/Users");

        await Assertions.Expect(_page.Locator("h1.page-title"))
            .ToHaveTextAsync("Người dùng");
        await Assertions.Expect(_page.GetByRole(AriaRole.Table)).ToBeVisibleAsync();

        // The seeded users must appear in the table.
        await Assertions.Expect(_page.GetByRole(AriaRole.Link, new() { Name = "Nguyễn Văn A", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByRole(AriaRole.Link, new() { Name = "Trần Thị B", Exact = true })).ToBeVisibleAsync();

        // The stat cards must show the total.
        await Assertions.Expect(_page.GetByText("Tổng người dùng")).ToBeVisibleAsync();

        AssertNoBrowserErrors();
    }

    [Fact]
    public async Task User_List_Shows_The_Empty_State_When_There_Is_No_Data()
    {
        await GoToAsync("/Users");

        await Assertions.Expect(_page.GetByText("Chưa có người dùng nào")).ToBeVisibleAsync();
        // The empty state offers a call to action (the page header has one as well).
        await Assertions.Expect(_page.Locator(".empty-state").GetByRole(AriaRole.Link, new() { Name = "Thêm người dùng" }))
            .ToBeVisibleAsync();

        AssertNoBrowserErrors();
    }

    [Fact]
    public async Task User_List_Shows_The_No_Result_State_When_The_Search_Misses()
    {
        await SeedUserAsync("Nguyễn Văn A", $"a-{Guid.NewGuid():N}@example.com", "0912345671");

        await GoToAsync("/Users");
        await _page.GetByRole(AriaRole.Searchbox).FillAsync("khong-ton-tai-abcxyz");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Tìm kiếm" }).ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(_page.Locator("h2.empty-title")).ToHaveTextAsync("Không tìm thấy kết quả");

        AssertNoBrowserErrors();
    }

    // ---------- search ----------

    [Fact]
    public async Task Search_Finds_A_User_By_Name_Ignoring_Diacritics()
    {
        await SeedUserAsync("Nguyễn Văn A", $"a-{Guid.NewGuid():N}@example.com", "0912345671");
        await SeedUserAsync("Trần Thị B", $"b-{Guid.NewGuid():N}@example.com", "0912345672");

        await GoToAsync("/Users");
        await _page.GetByRole(AriaRole.Searchbox).FillAsync("nguyen");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Tìm kiếm" }).ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(_page.GetByRole(AriaRole.Link, new() { Name = "Nguyễn Văn A", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByRole(AriaRole.Link, new() { Name = "Trần Thị B", Exact = true })).Not.ToBeVisibleAsync();

        AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Search_Finds_A_User_By_Email_And_Phone()
    {
        var email = $"findme-{Guid.NewGuid():N}@example.com";
        await SeedUserAsync("Tìm Theo Email", email, "0987654321");

        await GoToAsync("/Users");
        await _page.GetByRole(AriaRole.Searchbox).FillAsync(email);
        await _page.GetByRole(AriaRole.Button, new() { Name = "Tìm kiếm" }).ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(_page.GetByRole(AriaRole.Link, new() { Name = "Tìm Theo Email", Exact = true })).ToBeVisibleAsync();

        // Clear the filter and search by phone.
        await GoToAsync("/Users");
        await _page.GetByRole(AriaRole.Searchbox).FillAsync("0987654321");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Tìm kiếm" }).ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(_page.GetByRole(AriaRole.Link, new() { Name = "Tìm Theo Email", Exact = true })).ToBeVisibleAsync();

        AssertNoBrowserErrors();
    }

    [Fact]
    public async Task The_Search_Filter_Can_Be_Cleared()
    {
        await SeedUserAsync("Nguyễn Văn A", $"a-{Guid.NewGuid():N}@example.com", "0912345671");

        await GoToAsync("/Users?keyword=khong-co-ket-qua");
        await Assertions.Expect(_page.Locator("h2.empty-title")).ToHaveTextAsync("Không tìm thấy kết quả");

        await _page.GetByRole(AriaRole.Link, new() { Name = "Xóa lọc" }).First.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // After clearing the filter the seeded user is visible again.
        await Assertions.Expect(_page.GetByRole(AriaRole.Link, new() { Name = "Nguyễn Văn A", Exact = true })).ToBeVisibleAsync();

        AssertNoBrowserErrors();
    }

    // ---------- sorting ----------

    [Fact]
    public async Task The_Table_Can_Be_Sorted_By_Name()
    {
        await SeedUserAsync("Zeta Nguyễn", $"z-{Guid.NewGuid():N}@example.com", "0912345671");
        await SeedUserAsync("Alpha Trần", $"a-{Guid.NewGuid():N}@example.com", "0912345672");

        await GoToAsync("/Users?sortBy=fullName&sortOrder=asc");

        var rows = _page.Locator("table tbody tr");
        await Assertions.Expect(rows).ToHaveCountAsync(2);

        var firstCell = await rows.Nth(0).Locator("td").Nth(1).InnerTextAsync();
        firstCell.Should().Contain("Alpha Trần");

        AssertNoBrowserErrors();
    }

    // ---------- details page ----------

    [Fact]
    public async Task Clicking_A_User_Opens_The_Details_Page()
    {
        await SeedUserAsync("Nguyễn Chi Tiết", $"detail-{Guid.NewGuid():N}@example.com", "0912345673",
            "12 Lê Lợi, Quận 1, TP.HCM");

        await GoToAsync("/Users");
        await _page.GetByRole(AriaRole.Link, new() { Name = "Nguyễn Chi Tiết", Exact = true }).ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        _page.Url.Should().Contain("/Users/Details/");
        await Assertions.Expect(_page.Locator("h1.font-display")).ToHaveTextAsync("Nguyễn Chi Tiết");
        await Assertions.Expect(_page.GetByText("12 Lê Lợi, Quận 1, TP.HCM")).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByText("Chưa có giấy tờ tùy thân")).ToBeVisibleAsync();

        AssertNoBrowserErrors();
    }

    [Fact]
    public async Task The_Details_Page_Shows_The_Empty_Document_State()
    {
        await SeedUserAsync("Không Giấy Tờ", $"nodoc-{Guid.NewGuid():N}@example.com", "0912345674");

        await GoToAsync("/Users");
        await _page.GetByRole(AriaRole.Link, new() { Name = "Không Giấy Tờ", Exact = true }).ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(_page.GetByText("Chưa có giấy tờ tùy thân")).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByRole(AriaRole.Link, new() { Name = "Thêm giấy tờ đầu tiên" })).ToBeVisibleAsync();

        AssertNoBrowserErrors();
    }

    [Fact]
    public async Task An_Unknown_User_Id_Redirects_Back_To_The_List_With_An_Error()
    {
        await GoToAsync($"/Users/Details/{Guid.NewGuid()}");

        // The controller redirects to the list and surfaces the reason as an error alert.
        _page.Url.Should().Contain("/Users");
        await Assertions.Expect(_page.Locator(".alert-danger")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator(".alert-danger")).ToContainTextAsync("không tồn tại");
    }

    // ---------- responsive ----------

    [Fact]
    public async Task The_Layout_Adapts_To_A_Small_Screen()
    {
        await SeedUserAsync("Nguyễn Văn A", $"a-{Guid.NewGuid():N}@example.com", "0912345671");

        var small = await _context.NewPageAsync();
        await small.SetViewportSizeAsync(390, 844);
        await small.GotoAsync(_harness.Url("/Users"), new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        // The sidebar must be collapsed into a drawer on mobile, so the toggle opens it.
        var drawerToggle = small.Locator("#sidebarOpen");
        await Assertions.Expect(drawerToggle).ToBeVisibleAsync();
        await drawerToggle.ClickAsync();

        var sidebar = small.Locator("#sidebar");
        await Assertions.Expect(sidebar).ToHaveClassAsync(new Regex("is-open"));

        // The table must remain usable (responsive card layout).
        await Assertions.Expect(small.GetByRole(AriaRole.Table)).ToBeVisibleAsync();

        await small.CloseAsync();
        AssertNoBrowserErrors();
    }

    [Fact]
    public async Task The_Layout_Adapts_To_A_Desktop_Screen()
    {
        await SeedUserAsync("Nguyễn Văn A", $"a-{Guid.NewGuid():N}@example.com", "0912345671");

        await _page.SetViewportSizeAsync(1920, 1080);
        await GoToAsync("/Users");

        // On desktop the sidebar is always visible and the mobile toggle is hidden.
        await Assertions.Expect(_page.Locator("#sidebar")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("#sidebarOpen")).Not.ToBeVisibleAsync();

        AssertNoBrowserErrors();
    }

    // ---------- theme ----------

    [Fact]
    public async Task The_Theme_Toggle_Switches_Between_Light_And_Dark()
    {
        await GoToAsync("/Users");

        var html = _page.Locator("html");
        var initiallyDark = await html.GetAttributeAsync("class");

        await _page.Locator("#themeToggle").ClickAsync();
        var afterToggle = await html.GetAttributeAsync("class");
        afterToggle.Should().NotBe(initiallyDark);

        await _page.Locator("#themeToggle").ClickAsync();
        var afterSecondToggle = await html.GetAttributeAsync("class");
        afterSecondToggle.Should().Be(initiallyDark);
    }

    // ---------- helper ----------

    private void AssertNoBrowserErrors()
    {
        _consoleErrors.Should().BeEmpty(
            $"the page must not log console errors. HTTP errors: {string.Join(" | ", _httpErrors)}");
        _failedRequests.Should().BeEmpty("no request may fail");
        _httpErrors.Should().BeEmpty("no request may return an error status");
    }
}

[CollectionDefinition(nameof(BrowserTestCollection), DisableParallelization = true)]
public class BrowserTestCollection { }
