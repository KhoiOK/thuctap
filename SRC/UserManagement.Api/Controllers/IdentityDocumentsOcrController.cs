using Microsoft.AspNetCore.Mvc;
using UserManagement.Api.DTOs.Users.IdentityDocuments;
using UserManagement.Api.Ocr;
using UserManagement.Api.Services;

namespace UserManagement.Api.Controllers;

/// <summary>
/// OCR endpoints for identity documents (API-12 and API-13). The scan endpoint returns
/// normalized fields that the frontend auto-fills into the form; the confirm endpoint
/// turns the scan into a persisted identity document.
/// </summary>
[ApiController]
[Route("api/v1/users/{userId:guid}/identity-documents/ocr")]
public class IdentityDocumentsOcrController : ControllerBase
{
    private readonly IOcrService _ocrService;
    private readonly ILogger<IdentityDocumentsOcrController> _logger;

    public IdentityDocumentsOcrController(IOcrService ocrService, ILogger<IdentityDocumentsOcrController> logger)
    {
        _ocrService = ocrService;
        _logger = logger;
    }

    /// <summary>API-12: scan the front and back images and return the extracted fields.</summary>
    [HttpPost("scan", Name = "ScanIdentityDocumentOcr")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(25_000_000)] // 2x 10MB scan images + form fields
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Scan(
        [FromRoute] Guid userId,
        [FromForm] OcrScanRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _ocrService.ScanAsync(userId, request, cancellationToken);
            return Ok(result);
        }
        catch (OcrUnreadableException ex)
        {
            return Problem(
                title: "Không đọc được thông tin giấy tờ.",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://tools.ietf.org/html/rfc9110#section-15.5.1");
        }
        catch (OcrUnavailableException ex)
        {
            _logger.LogWarning("OCR unavailable for user {UserId}: {Reason}", userId, ex.Message);
            return Problem(
                title: "Dịch vụ quét tự động đang không khả dụng.",
                detail: "Bạn có thể nhập thông tin giấy tờ thủ công và lưu bình thường.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                type: "https://tools.ietf.org/html/rfc9110#section-15.6.4");
        }
    }

    /// <summary>API-13: confirm a scan and create the identity document from it.</summary>
    [HttpPost("confirm", Name = "ConfirmIdentityDocumentOcr")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm(
        [FromRoute] Guid userId,
        [FromBody] OcrConfirmRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _ocrService.ConfirmAsync(userId, request, cancellationToken);

        // The Location header must point at the created document. The route NAME is
        // "GetIdentityDocument" while the action METHOD is called "GetById" - so
        // CreatedAtRoute (which takes the route name) must be used here. Passing the
        // route name to CreatedAtAction (which expects the method name) makes the link
        // generation fail with "No route matches the supplied values" and the endpoint
        // answers 500 instead of 201.
        return CreatedAtRoute(
            "GetIdentityDocument",
            new { userId, documentId = created.Id },
            created);
    }
}
