using Microsoft.AspNetCore.Mvc;
using UserManagement.Api.DTOs.Users.IdentityDocuments;
using UserManagement.Api.Ocr;
using UserManagement.Api.Services;

namespace UserManagement.Api.Controllers;

/// <summary>
/// OCR preview endpoint (API-12b). It runs the OCR engine on the uploaded images and
/// returns the extracted fields WITHOUT requiring a user and WITHOUT persisting
/// anything. It backs the "Quick Create" flow, where the form has to be pre-filled
/// before the user record exists.
/// </summary>
[ApiController]
[Route("api/v1/identity-documents/ocr")]
public class IdentityDocumentsOcrPreviewController : ControllerBase
{
    private readonly IOcrService _ocrService;
    private readonly ILogger<IdentityDocumentsOcrPreviewController> _logger;

    public IdentityDocumentsOcrPreviewController(
        IOcrService ocrService,
        ILogger<IdentityDocumentsOcrPreviewController> logger)
    {
        _ocrService = ocrService;
        _logger = logger;
    }

    /// <summary>
    /// API-12b: preview the extracted fields of an identity document without creating a scan.
    /// </summary>
    [HttpPost("preview", Name = "PreviewIdentityDocumentOcr")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(25_000_000)] // 2x 10MB scan images + form fields
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Preview(
        [FromForm] OcrPreviewRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _ocrService.PreviewAsync(request, cancellationToken);
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
            _logger.LogWarning("OCR preview unavailable: {Reason}", ex.Message);
            return Problem(
                title: "Dịch vụ quét tự động đang không khả dụng.",
                detail: "Bạn có thể nhập thông tin giấy tờ thủ công và lưu bình thường.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                type: "https://tools.ietf.org/html/rfc9110#section-15.6.4");
        }
    }
}
