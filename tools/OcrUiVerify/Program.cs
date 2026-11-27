using Microsoft.Playwright;

// Verifies the Quick Create OCR flow in a real browser with a REAL CCCD photo.
//
//   dotnet run --project tools/OcrUiVerify -- <front.jpg> <back.jpg>
//
// It drives the page the same way a user does: pick the OCR mode, upload the two
// photos, press the OCR button and read the values that landed in the form.

var front = args.Length > 0 ? args[0] : throw new ArgumentException("front image required");
var back = args.Length > 1 ? args[1] : throw new ArgumentException("back image required");
var baseUrl = args.Length > 2 ? args[2] : "http://localhost:5052";

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 900 } });
var page = await context.NewPageAsync();

var consoleErrors = new List<string>();
var httpErrors = new List<string>();

page.Console += (_, msg) => { if (msg.Type == "error") consoleErrors.Add(msg.Text); };
page.RequestFailed += (_, req) => httpErrors.Add($"{req.Method} {req.Url}: {req.Failure}");

await page.GotoAsync($"{baseUrl}/Users/QuickCreate", new() { WaitUntil = WaitUntilState.NetworkIdle });

// ---- Switch to the OCR mode ----
await page.Locator("[data-mode-option='ocr']").ClickAsync();
Console.WriteLine("[ok] switched to OCR mode");

// ---- Upload the two photos ----
await page.Locator("input[data-ocr-image='front']").SetInputFilesAsync(front);
await page.Locator("input[data-ocr-image='back']").SetInputFilesAsync(back);
Console.WriteLine("[ok] uploaded the two photos");

// ---- Run the OCR ----
var button = page.Locator("[data-ocr-preview-btn]");
await button.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

var enabled = false;
for (var i = 0; i < 30 && !enabled; i++)
{
    enabled = await button.IsEnabledAsync();
    if (!enabled) await Task.Delay(500);
}

if (!enabled)
{
    Console.WriteLine("[FAIL] the OCR button never became enabled");
    await page.ScreenshotAsync(new() { Path = "ocr-ui-fail.png", FullPage = true });
    return 1;
}

var response = await page.RunAndWaitForResponseAsync(
    async () => await button.ClickAsync(),
    r => r.Url.Contains("OcrPreview") || r.Url.Contains("ocr/preview"),
    new() { Timeout = 180_000 });

var body = await response.TextAsync();
Console.WriteLine($"[{(response.Ok ? "ok" : "FAIL")}] preview -> HTTP {(int)response.Status}");
Console.WriteLine($"      {body}");

// ---- Wait for the result panel ----
try
{
    await page.Locator("[data-ocr-panel]").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
    Console.WriteLine("[ok] the OCR result panel is visible");
}
catch (TimeoutException)
{
    Console.WriteLine("[FAIL] the OCR result panel never appeared");
}

// ---- Read the values that landed in the form ----
async Task<string?> ValueOf(string selector)
{
    var locator = page.Locator(selector);
    return await locator.CountAsync() > 0 ? await locator.InputValueAsync() : null;
}

var fields = new (string Label, string Selector)[]
{
    ("Họ và tên", "#FullName"),
    ("Ngày sinh", "#DateOfBirth"),
    ("Giới tính", "#Gender"),
    ("Địa chỉ", "#Address"),
    ("Số giấy tờ", "#DocumentNumber"),
    ("Ngày cấp", "#IssuedDate"),
    ("Ngày hết hạn", "#ExpiryDate"),
};

Console.WriteLine("\n--- the values the form received ---");
foreach (var (label, selector) in fields)
{
    var value = await ValueOf(selector);
    Console.WriteLine($"  {label,-14} = {value ?? "(trống)"}");
}

var screenshot = "ocr-ui-result.png";
await page.ScreenshotAsync(new() { Path = screenshot, FullPage = true });
Console.WriteLine($"\n[screenshot] {screenshot}");

var with503 = consoleErrors.Concat(httpErrors).Where(e => e.Contains("503")).ToList();
if (with503.Count > 0)
{
    Console.WriteLine("[FAIL] a 503 was reported:");
    foreach (var error in with503) Console.WriteLine($"      {error}");
    return 1;
}

Console.WriteLine("\n[ok] no 503 anywhere - the OCR service was reachable");
return 0;
