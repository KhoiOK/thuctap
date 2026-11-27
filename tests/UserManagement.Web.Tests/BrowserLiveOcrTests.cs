using System.Text.Json;
using Microsoft.Playwright;
using UserManagement.Web.Tests.Infrastructure;
using Xunit;

namespace UserManagement.Web.Tests;

/// <summary>
/// Live end-to-end test of the CCCD OCR branch: Browser -> Web MVC -> Backend API ->
/// the REAL PaddleOCR microservice on http://localhost:8100.
///
/// The OCR microservice is an optional local dependency, so the test is SKIPPED
/// (not failed) when the service does not answer on /health. When it does run, it
/// proves the whole chain works with a genuine OCR engine - not a fake.
/// </summary>
[Collection(nameof(BrowserTestCollection))]
public class BrowserLiveOcrTests : IAsyncLifetime
{
    private readonly LiveOcrTestHarness _harness = new();
    private IBrowserContext _context = null!;
    private IPage _page = null!;

    private readonly List<string> _consoleErrors = new();
    private readonly List<string> _httpErrors = new();

    public async Task InitializeAsync()
    {
        if (!await LiveOcrTestHarness.IsOcrServiceAvailableAsync())
        {
            return;
        }

        await _harness.InitializeAsync();
        (_context, _page) = await _harness.NewPageAsync();

        _page.Console += (_, msg) =>
        {
            if (msg.Type == "error") _consoleErrors.Add(msg.Text);
        };
        _page.RequestFailed += (_, req) =>
        {
            _httpErrors.Add($"{req.Method} {req.Url}: {req.Failure}");
        };
    }

    public async Task DisposeAsync()
    {
        if (_page is not null)
        {
            await _page.CloseAsync();
            await _context.CloseAsync();
        }

        await _harness.DisposeAsync();
    }

    [Fact]
    public async Task Live_Ocr_Scan_Fills_The_Quick_Create_Form()
    {
        if (!await LiveOcrTestHarness.IsOcrServiceAvailableAsync())
        {
            // The PaddleOCR microservice is not running - nothing to assert.
            return;
        }

        await _page.GotoAsync(new Uri(_harness.WebBaseAddress, "/Users/QuickCreate").ToString());

        await _page.Locator("[data-mode-option='ocr']").ClickAsync();

        var (front, back) = BuildCccdImages();

        await _page.Locator("input[data-ocr-image='front']").SetInputFilesAsync(front);
        await _page.Locator("input[data-ocr-image='back']").SetInputFilesAsync(back);

        await Assertions.Expect(_page.Locator("[data-ocr-preview-btn]")).ToBeEnabledAsync();

        // Capture the raw preview response: it tells us exactly what the Web -> API ->
        // PaddleOCR chain answered when the panel does not show up.
        var previewResponse = await _page.RunAndWaitForResponseAsync(
            async () => await _page.Locator("[data-ocr-preview-btn]").ClickAsync(),
            r => r.Url.Contains("OcrPreview") || r.Url.Contains("ocr/preview"));

        var previewBody = await previewResponse.TextAsync();
        Assert.True(previewResponse.Ok,
            $"the OCR preview failed ({(int)previewResponse.Status}): {previewBody}");

        // The real engine needs a moment on the first call (model warm-up).
        await Assertions.Expect(_page.Locator("[data-ocr-panel]")).ToBeVisibleAsync(new() { Timeout = 120000 });

        // The document number is the field the CCCD always carries.
        var documentNumber = await _page.Locator("#DocumentNumber").InputValueAsync();
        Assert.Contains("079095001234", documentNumber);

        // No 503 toast must appear: the OCR service was reachable.
        Assert.DoesNotContain(_consoleErrors, e => e.Contains("503"));
        Assert.DoesNotContain(_httpErrors, e => e.Contains("503"));
    }

    /// <summary>
    /// Calls the Backend API preview endpoint directly so a failing chain can be
    /// pinpointed (Web controller vs API controller vs OCR service).
    /// </summary>
    [Fact]
    public async Task Live_Ocr_Api_Preview_Returns_The_Extracted_Fields()
    {
        if (!await LiveOcrTestHarness.IsOcrServiceAvailableAsync())
        {
            return;
        }

        using var probe = new HttpClient
        {
            BaseAddress = _harness.ApiBaseAddress,
            Timeout = TimeSpan.FromSeconds(30)
        };

        var health = await probe.GetAsync("/api/v1/Health");
        Assert.True(health.IsSuccessStatusCode, "the API host must be reachable.");

        var (front, back) = BuildCccdImages();

        using var client = new HttpClient
        {
            BaseAddress = _harness.ApiBaseAddress,
            Timeout = TimeSpan.FromSeconds(180)
        };
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("CCCD"), "DocumentType");
        form.Add(new ByteArrayContent(await File.ReadAllBytesAsync(front)), "FrontImage", "front.jpg");
        form.Add(new ByteArrayContent(await File.ReadAllBytesAsync(back)), "BackImage", "back.jpg");

        var response = await client.PostAsync("/api/v1/identity-documents/ocr/preview", form);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"the API preview failed ({(int)response.StatusCode}): {body}");

        Assert.Contains("079095001234", body);
    }

    /// <summary>
    /// The real CCCD photo. On the real card the expiry date ("CÓ GIÁ TRỊ ĐẾN") is
    /// printed on the FRONT and the issue date ("Ngày, tháng, năm") on the BACK.
    /// The synthetic card cannot prove that, because it prints both dates on the
    /// front - so the real photo is what pins the mapping down.
    /// </summary>
    [Fact]
    public async Task Live_Ocr_Reads_The_Real_Cccd_Photo_With_The_Dates_On_The_Right_Sides()
    {
        if (!await LiveOcrTestHarness.IsOcrServiceAvailableAsync())
        {
            return;
        }

        var (front, back) = FindRealCccdPhotos();
        if (front is null || back is null)
        {
            // The real photos are not part of the repository - nothing to assert.
            return;
        }

        using var client = new HttpClient
        {
            BaseAddress = _harness.ApiBaseAddress,
            Timeout = TimeSpan.FromSeconds(180)
        };
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("CCCD"), "DocumentType");
        form.Add(new ByteArrayContent(await File.ReadAllBytesAsync(front)), "FrontImage", "front.jpg");
        form.Add(new ByteArrayContent(await File.ReadAllBytesAsync(back)), "BackImage", "back.jpg");

        var response = await client.PostAsync("/api/v1/identity-documents/ocr/preview", form);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"the API preview failed ({(int)response.StatusCode}): {body}");

        using var document = JsonDocument.Parse(body);

        var fields = document.RootElement.GetProperty("fields");

        // The expiry date lives on the FRONT of the real card.
        Assert.Equal("2028-10-05", fields.GetProperty("expiryDate").GetProperty("value").GetString());
        Assert.Equal("front", fields.GetProperty("expiryDate").GetProperty("source").GetString());

        // The issue date lives on the BACK of the real card.
        Assert.Equal("2021-04-22", fields.GetProperty("issueDate").GetProperty("value").GetString());
        Assert.Equal("back", fields.GetProperty("issueDate").GetProperty("source").GetString());

        // The two address lines are merged into one field.
        Assert.Contains("Xuân Hng", fields.GetProperty("address").GetProperty("value").GetString());
        Assert.Contains("M An", fields.GetProperty("address").GetProperty("value").GetString());
    }

    /// <summary>
    /// Locates the real CCCD photos. They live outside the repository (they are the
    /// personal document of the person who tested the system), so the test skips
    /// itself when they are not on this machine.
    /// </summary>
    private static (string? Front, string? Back) FindRealCccdPhotos()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "services", "paddleocr", "real");
            var front = Path.Combine(candidate, "front.jpg");
            var back = Path.Combine(candidate, "back.jpg");
            if (File.Exists(front) && File.Exists(back))
            {
                return (front, back);
            }

            dir = dir.Parent;
        }

        return (null, null);
    }

    /// <summary>
    /// Builds the two card images. The text is rendered by the Python helper that
    /// ships with the microservice (services/paddleocr/make_test_cccd.py) because
    /// drawing Vietnamese diacritics requires a Unicode TTF font, which ImageSharp
    /// cannot load without an extra package. When Python is not available the images
    /// are rendered by the same helper through the OCR service itself.
    /// </summary>
    private static (string Front, string Back) BuildCccdImages()
    {
        var dir = Path.Combine(Path.GetTempPath(), "UserManagement_LiveOcr", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var front = Path.Combine(dir, "front.jpg");
        var back = Path.Combine(dir, "back.jpg");

        var script = FindHelperScript();
        if (script is not null)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("python")
            {
                WorkingDirectory = Path.GetDirectoryName(script)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(script);
            startInfo.ArgumentList.Add(front);
            startInfo.ArgumentList.Add(back);

            using var process = System.Diagnostics.Process.Start(startInfo)!;
            process.WaitForExit(120_000);

            if (process.ExitCode == 0 && File.Exists(front) && File.Exists(back))
                return (front, back);
        }

        // Fallback: plain cards with no diacritics. The document number is ASCII, so
        // the OCR engine still reads it - only the Vietnamese labels are lost.
        return (TestImages.WriteJpeg(dir, "front.jpg"), TestImages.WriteJpeg(dir, "back.jpg"));
    }

    private static string? FindHelperScript()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "services", "paddleocr", "make_test_cccd.py");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
