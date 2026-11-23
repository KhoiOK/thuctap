using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using UserManagement.Api.DTOs.Users.IdentityDocuments;
using UserManagement.Api.Exceptions;
using UserManagement.Api.Services;

namespace UserManagement.Api.Controllers;

/// <summary>
/// Identity documents management for a user (API-06 to API-11). All endpoints are nested
/// under <c>/api/v1/users/{userId}/identity-documents</c> per API-Spec §3.
/// </summary>
[ApiController]
[Route("api/v1/users/{userId:guid}/identity-documents")]
[Consumes("application/json")]
public class IdentityDocumentsController : ControllerBase
{
    private readonly IIdentityDocumentService _service;
    private readonly ILogger<IdentityDocumentsController> _logger;

    public IdentityDocumentsController(IIdentityDocumentService service, ILogger<IdentityDocumentsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>API-06: list all identity documents for a user.</summary>
    [HttpGet(Name = "ListIdentityDocuments")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        var docs = await _service.ListAsync(userId, cancellationToken);
        return Ok(docs);
    }

    /// <summary>API-11: get a single identity document.</summary>
    [HttpGet("{documentId:guid}", Name = "GetIdentityDocument")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        [FromRoute] Guid userId,
        [FromRoute] Guid documentId,
        CancellationToken cancellationToken)
    {
        var doc = await _service.GetByIdAsync(userId, documentId, cancellationToken);
        return Ok(doc);
    }

    /// <summary>API-07: create an identity document with multipart upload.</summary>
    [HttpPost(Name = "CreateIdentityDocument")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(15_000_000)] // 2x 5MB images + form fields
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromRoute] Guid userId,
        [FromForm] CreateIdentityDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _service.CreateAsync(userId, request, cancellationToken);
        return CreatedAtAction(
            nameof(GetById),
            new { userId, documentId = created.Id },
            created);
    }

    /// <summary>API-08: replace an identity document's metadata and optionally its images.</summary>
    [HttpPut("{documentId:guid}", Name = "UpdateIdentityDocument")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(15_000_000)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        [FromRoute] Guid userId,
        [FromRoute] Guid documentId,
        [FromForm] UpdateIdentityDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _service.UpdateAsync(userId, documentId, request, cancellationToken);
        return Ok(updated);
    }

    /// <summary>API-09: delete an identity document and its files.</summary>
    [HttpDelete("{documentId:guid}", Name = "DeleteIdentityDocument")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        [FromRoute] Guid userId,
        [FromRoute] Guid documentId,
        CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(userId, documentId, cancellationToken);
        return NoContent();
    }

    /// <summary>API-10: stream a document image, with ETag/If-None-Match support (304 Not Modified).</summary>
    [HttpGet("{documentId:guid}/images/{side}", Name = "GetIdentityDocumentImage")]
    [Produces("image/jpeg", "image/png")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetImage(
        [FromRoute] Guid userId,
        [FromRoute] Guid documentId,
        [FromRoute] string side,
        CancellationToken cancellationToken)
    {
        if (side is not ("front" or "back"))
        {
            return NotFound();
        }

        var result = await _service.OpenImageAsync(userId, documentId, side, cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        // SRS v2.0 §6.1: every image view MUST be logged (timestamp, source address,
        // userId, documentId, side). The document number is never written to the log.
        LogImageAccess(userId, documentId, side, result.ETag);

        Response.Headers[HeaderNames.ETag] = result.ETag;
        Response.Headers[HeaderNames.LastModified] = result.LastModifiedUtc.ToString("R");
        // SRS v2.0 §4.3 #10 - the image must always be the newest version.
        Response.Headers[HeaderNames.CacheControl] = "private, no-cache";

        var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrEmpty(ifNoneMatch))
        {
            var clientTag = ifNoneMatch.Trim();
            if (string.Equals(clientTag, result.ETag, StringComparison.Ordinal)
                || clientTag == "*")
            {
                return StatusCode(StatusCodes.Status304NotModified);
            }
        }

        return File(result.Stream, result.ContentType);
    }

    /// <summary>
    /// Writes the mandatory image-access audit entry. The client IP is taken from
    /// <c>X-Forwarded-For</c> when a reverse proxy is in front of the API.
    /// </summary>
    private void LogImageAccess(Guid userId, Guid documentId, string side, string etag)
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var forwarded = Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            remoteIp = forwarded.Split(',')[0].Trim();
        }

        _logger.LogInformation(
            "Image access: userId={UserId} documentId={DocumentId} side={Side} ip={RemoteIp} etag={ETag}",
            userId, documentId, side, remoteIp, etag);
    }
}
