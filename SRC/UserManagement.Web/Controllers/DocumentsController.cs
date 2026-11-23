using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using UserManagement.Web.Services;
using UserManagement.Web.ViewModels.Users;

namespace UserManagement.Web.Controllers;

/// <summary>
/// UI-Spec §2: identity-document routes nested under /Users/{userId}/Documents/...
/// All form actions post to API-07/08/09; the image route proxies API-10.
/// </summary>
[Route("Users/{userId:guid}/Documents")]
public class DocumentsController : Controller
{
    private readonly IUsersApiClient _api;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(IUsersApiClient api, ILogger<DocumentsController> logger)
    {
        _api = api;
        _logger = logger;
    }

    // =================== SCR-06: Create ===================

    [HttpGet("Create")]
    public IActionResult Create(Guid userId, [FromQuery] string? returnUrl)
    {
        var vm = new IdentityDocumentFormViewModel
        {
            UserId = userId,
            DocumentType = "CCCD",
            ReturnUrl = SanitizeReturnUrl(returnUrl)
        };
        return View("Form", vm);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(15_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 15_000_000)]
    public async Task<IActionResult> Create(Guid userId, IdentityDocumentFormViewModel vm, CancellationToken cancellationToken = default)
    {
        vm.UserId = userId;
        vm.ReturnUrl = SanitizeReturnUrl(vm.ReturnUrl);
        vm.DocumentNumber = vm.DocumentNumber?.Trim() ?? string.Empty;

        if (!ModelState.IsValid) return View("Form", vm);

        try
        {
            var result = await _api.CreateDocumentAsync(
                userId, vm.DocumentType, vm.DocumentNumber,
                vm.IssuedDate, vm.ExpiryDate, vm.FrontImage, vm.BackImage,
                cancellationToken);

            if (result.IsSuccess)
            {
                TempData["Toast"] = "Đã thêm giấy tờ.";
                return RedirectToAction("Details", "Users", new { id = userId, returnUrl = vm.ReturnUrl });
            }
            if (result.StatusCode == 404)
            {
                TempData["Error"] = "Người dùng không tồn tại hoặc đã bị xóa.";
                return RedirectToAction("Index", "Users");
            }
            if (result.FieldErrors.Count > 0)
            {
                foreach (var kv in result.FieldErrors)
                {
                    ModelState.AddModelError(kv.Key, string.Join("; ", kv.Value));
                }
            }
            vm.FieldErrors = result.FieldErrors.ToDictionary(kv => kv.Key, kv => kv.Value);
            vm.ErrorMessage ??= result.GetDisplayMessage();
        }
        catch (ApiTransportException ex)
        {
            vm.ErrorMessage = ex.Message;
        }
        return View("Form", vm);
    }

    // =================== SCR-07: OCR scan ===================

    /// <summary>
    /// Runs the automatic scan (API-12) and returns the extracted fields as JSON so the
    /// form can auto-fill the values. A failure never blocks the form - the user can
    /// always enter the data manually.
    /// </summary>
    [HttpPost("Create/{documentId:guid}/OcrScan")]
    [HttpPost("Create/OcrScan")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(25_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 25_000_000)]
    public async Task<IActionResult> OcrScan(
        Guid userId,
        [FromForm] string documentType,
        [FromForm] IFormFile? frontImage,
        [FromForm] IFormFile? backImage,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _api.ScanOcrAsync(userId, documentType, frontImage, backImage, cancellationToken);
            if (result is null)
            {
                return StatusCode(StatusCodes.Status400BadRequest, new
                {
                    ok = false,
                    message = "Không đọc được thông tin giấy tờ. Bạn vẫn có thể nhập thủ công."
                });
            }

            return Json(new { ok = true, data = result });
        }
        catch (ApiTransportException ex)
        {
            _logger.LogWarning(ex, "Transport error during the OCR scan for user {UserId}.", userId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                ok = false,
                message = "Dịch vụ quét tự động đang không khả dụng. Bạn có thể nhập thông tin thủ công."
            });
        }
    }

    // =================== SCR-06: Edit ===================

    [HttpGet("Edit/{documentId:guid}")]
    public async Task<IActionResult> Edit(Guid userId, Guid documentId, [FromQuery] string? returnUrl, CancellationToken cancellationToken = default)
    {
        var vm = new IdentityDocumentFormViewModel
        {
            UserId = userId,
            DocumentId = documentId,
            ReturnUrl = SanitizeReturnUrl(returnUrl)
        };
        try
        {
            var result = await _api.GetDocumentAsync(userId, documentId, cancellationToken);
            if (result.IsSuccess && result.Value is not null)
            {
                var d = result.Value;
                vm.DocumentType = d.DocumentType;
                vm.DocumentNumber = d.DocumentNumber;
                vm.IssuedDate = d.IssuedDate;
                vm.ExpiryDate = d.ExpiryDate;
                vm.ExistingFrontImageUrl = d.FrontImageUrl;
                vm.ExistingBackImageUrl = d.BackImageUrl;
            }
            else if (result.StatusCode == 404)
            {
                TempData["Error"] = "Giấy tờ không tồn tại.";
                return RedirectToAction("Details", "Users", new { id = userId, returnUrl = vm.ReturnUrl });
            }
            else
            {
                vm.ErrorMessage = result.GetDisplayMessage();
            }
        }
        catch (ApiTransportException ex)
        {
            vm.ErrorMessage = ex.Message;
        }
        return View("Form", vm);
    }

    [HttpPost("Edit/{documentId:guid}")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(15_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 15_000_000)]
    public async Task<IActionResult> Edit(Guid userId, Guid documentId, IdentityDocumentFormViewModel vm, CancellationToken cancellationToken = default)
    {
        vm.UserId = userId;
        vm.DocumentId = documentId;
        vm.ReturnUrl = SanitizeReturnUrl(vm.ReturnUrl);
        vm.DocumentNumber = vm.DocumentNumber?.Trim() ?? string.Empty;

        if (!ModelState.IsValid) return View("Form", vm);

        try
        {
            var result = await _api.UpdateDocumentAsync(
                userId, documentId, vm.DocumentNumber,
                vm.IssuedDate, vm.ExpiryDate, vm.FrontImage, vm.BackImage,
                cancellationToken);

            if (result.IsSuccess)
            {
                TempData["Toast"] = "Đã lưu thay đổi.";
                return RedirectToAction("Details", "Users", new { id = userId, returnUrl = vm.ReturnUrl });
            }
            if (result.StatusCode == 404)
            {
                TempData["Error"] = "Giấy tờ không tồn tại.";
                return RedirectToAction("Details", "Users", new { id = userId, returnUrl = vm.ReturnUrl });
            }
            if (result.FieldErrors.Count > 0)
            {
                foreach (var kv in result.FieldErrors)
                {
                    ModelState.AddModelError(kv.Key, string.Join("; ", kv.Value));
                }
            }
            vm.FieldErrors = result.FieldErrors.ToDictionary(kv => kv.Key, kv => kv.Value);
            vm.ErrorMessage ??= result.GetDisplayMessage();
        }
        catch (ApiTransportException ex)
        {
            vm.ErrorMessage = ex.Message;
        }
        return View("Form", vm);
    }

    // =================== SCR-05: Delete ===================
    [HttpPost("Delete/{documentId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid userId, Guid documentId, [FromForm] string? returnUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _api.DeleteDocumentAsync(userId, documentId, cancellationToken);
            if (result.IsSuccess)
            {
                TempData["Toast"] = "Đã xóa giấy tờ.";
                var safe = SanitizeReturnUrl(returnUrl);
                if (!string.IsNullOrEmpty(safe)) return Redirect(safe);
                return RedirectToAction("Details", "Users", new { id = userId });
            }
            TempData["Error"] = result.GetDisplayMessage();
        }
        catch (ApiTransportException ex)
        {
            TempData["Error"] = ex.Message;
        }
        var safe2 = SanitizeReturnUrl(returnUrl);
        if (!string.IsNullOrEmpty(safe2)) return Redirect(safe2);
        return RedirectToAction("Details", "Users", new { id = userId });
    }

    // =================== API-10: Image proxy ===================

    [HttpGet("{documentId:guid}/Image/{side}")]
    public async Task<IActionResult> Image(Guid userId, Guid documentId, string side, CancellationToken cancellationToken = default)
    {
        if (side is not ("front" or "back")) return NotFound();

        EntityTagHeaderValue? ifNoneMatch = null;
        if (Request.Headers.TryGetValue("If-None-Match", out var inm) && !string.IsNullOrEmpty(inm))
        {
            if (EntityTagHeaderValue.TryParse(inm.ToString(), out var parsed))
            {
                ifNoneMatch = parsed;
            }
        }

        var response = await _api.GetDocumentImageAsync(userId, documentId, side, ifNoneMatch, cancellationToken);
        if (response is null) return NotFound();

        // Copy any caching/ETag headers from the API to our proxy response.
        if (response.Headers.ETag is not null) Response.Headers.ETag = response.Headers.ETag.ToString();
        if (response.Content.Headers.LastModified is { } lm) Response.Headers.LastModified = lm.ToString("R");

        if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
        {
            response.Dispose();
            return StatusCode(StatusCodes.Status304NotModified);
        }

        var contentType = response.Content.Headers.ContentType?.ToString() ?? "image/jpeg";
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return File(stream, contentType);
    }

    // =================== helpers ===================

    private string? SanitizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)) return null;
        if (!Url.IsLocalUrl(returnUrl)) return null;
        if (!returnUrl.StartsWith("/Users", StringComparison.OrdinalIgnoreCase)) return null;
        return returnUrl;
    }
}
