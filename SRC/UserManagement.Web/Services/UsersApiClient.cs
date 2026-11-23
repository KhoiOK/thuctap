using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UserManagement.Web.Services;

/// <summary>
/// Thrown when the API client itself cannot reach the Backend (network error, DNS,
/// timeout, TLS). Distinct from API-level errors (4xx/5xx with a Problem Details
/// body) which are surfaced as <see cref="ApiResult.Failure"/>.
/// </summary>
public sealed class ApiTransportException : Exception
{
    public ApiTransportException(string message, Exception inner) : base(message, inner) { }
}

public interface IUsersApiClient
{
    Task<ApiResult<PagedResultDto<UserListItemDto>>> ListAsync(
        string? keyword, int page, int pageSize, string sortBy, string sortOrder,
        CancellationToken cancellationToken = default);

    Task<ApiResult<UserDetailDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ApiResult<UserDetailDto>> CreateAsync(UserUpsertRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<UserDetailDto>> UpdateAsync(Guid id, UserUpsertRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // Identity documents

    Task<ApiResult<IReadOnlyList<IdentityDocumentListItemDto>>> ListDocumentsAsync(
        Guid userId, CancellationToken cancellationToken = default);

    Task<ApiResult<IdentityDocumentListItemDto>> GetDocumentAsync(
        Guid userId, Guid documentId, CancellationToken cancellationToken = default);

    Task<ApiResult<IdentityDocumentListItemDto>> CreateDocumentAsync(
        Guid userId, string documentType, string documentNumber,
        DateOnly? issuedDate, DateOnly? expiryDate,
        IFormFile? frontImage, IFormFile? backImage,
        CancellationToken cancellationToken = default);

    Task<ApiResult<IdentityDocumentListItemDto>> UpdateDocumentAsync(
        Guid userId, Guid documentId, string documentNumber,
        DateOnly? issuedDate, DateOnly? expiryDate,
        IFormFile? frontImage, IFormFile? backImage,
        CancellationToken cancellationToken = default);

    Task<ApiResult> DeleteDocumentAsync(Guid userId, Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams an identity document image (front/back). The MVC image proxy uses this to
    /// relay bytes from the API to the browser. Returns <c>null</c> if the API returns
    /// 404 or any non-2xx response.
    /// </summary>
    Task<HttpResponseMessage?> GetDocumentImageAsync(
        Guid userId, Guid documentId, string side,
        EntityTagHeaderValue? ifNoneMatch, CancellationToken cancellationToken = default);

    // OCR

    /// <summary>
    /// Scans the front and back images of an identity document and returns the extracted
    /// fields (API-12). Returns <c>null</c> when the OCR service is unavailable or the
    /// images could not be read - the caller then keeps the manual entry flow.
    /// </summary>
    Task<OcrScanResponseDto?> ScanOcrAsync(
        Guid userId, string documentType,
        IFormFile? frontImage, IFormFile? backImage,
        CancellationToken cancellationToken = default);

    /// <summary>Confirms a scan and creates the identity document from it (API-13).</summary>
    Task<ApiResult<IdentityDocumentListItemDto>> ConfirmOcrAsync(
        Guid userId, OcrConfirmRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Typed client for the Backend User/IdentityDocument API. Wraps HttpClient with
/// JSON serialization, Problem Details parsing, and translation to <see cref="ApiResult{T}"/>.
/// </summary>
public class UsersApiClient : IUsersApiClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    private readonly IApiClient _client;
    private readonly ILogger<UsersApiClient> _logger;

    public UsersApiClient(IApiClient client, ILogger<UsersApiClient> logger)
    {
        _client = client;
        _logger = logger;
    }

    // ============== USERS ==============

    public async Task<ApiResult<PagedResultDto<UserListItemDto>>> ListAsync(
        string? keyword, int page, int pageSize, string sortBy, string sortOrder,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/v1/users?page={page}&pageSize={pageSize}&sortBy={Uri.EscapeDataString(sortBy)}&sortOrder={Uri.EscapeDataString(sortOrder)}";
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            url += $"&keyword={Uri.EscapeDataString(keyword)}";
        }
        return await GetJsonAsync<PagedResultDto<UserListItemDto>>(url, cancellationToken);
    }

    public Task<ApiResult<UserDetailDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => GetJsonAsync<UserDetailDto>($"api/v1/users/{id}", cancellationToken);

    public async Task<ApiResult<UserDetailDto>> CreateAsync(UserUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var response = await SendJsonAsync(HttpMethod.Post, "api/v1/users", request, cancellationToken);
        return await ReadJsonAsync<UserDetailDto>(response, cancellationToken);
    }

    public async Task<ApiResult<UserDetailDto>> UpdateAsync(Guid id, UserUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var response = await SendJsonAsync(HttpMethod.Put, $"api/v1/users/{id}", request, cancellationToken);
        return await ReadJsonAsync<UserDetailDto>(response, cancellationToken);
    }

    public async Task<ApiResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await SendNoBodyAsync(HttpMethod.Delete, $"api/v1/users/{id}", cancellationToken);
        return await ReadNoBodyAsync(response);
    }

    // ============== IDENTITY DOCUMENTS ==============

    public Task<ApiResult<IReadOnlyList<IdentityDocumentListItemDto>>> ListDocumentsAsync(Guid userId, CancellationToken cancellationToken = default)
        => GetJsonAsync<IReadOnlyList<IdentityDocumentListItemDto>>($"api/v1/users/{userId}/identity-documents", cancellationToken);

    public Task<ApiResult<IdentityDocumentListItemDto>> GetDocumentAsync(Guid userId, Guid documentId, CancellationToken cancellationToken = default)
        => GetJsonAsync<IdentityDocumentListItemDto>($"api/v1/users/{userId}/identity-documents/{documentId}", cancellationToken);

    public async Task<ApiResult<IdentityDocumentListItemDto>> CreateDocumentAsync(
        Guid userId, string documentType, string documentNumber,
        DateOnly? issuedDate, DateOnly? expiryDate,
        IFormFile? frontImage, IFormFile? backImage,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(documentType), "documentType");
        form.Add(new StringContent(documentNumber), "documentNumber");
        if (issuedDate is { } i) form.Add(new StringContent(i.ToString("yyyy-MM-dd")), "issuedDate");
        if (expiryDate is { } e) form.Add(new StringContent(e.ToString("yyyy-MM-dd")), "expiryDate");

        if (frontImage is { Length: > 0 })
        {
            var content = new StreamContent(frontImage.OpenReadStream());
            content.Headers.ContentType = new MediaTypeHeaderValue(frontImage.ContentType ?? "application/octet-stream");
            form.Add(content, "frontImage", frontImage.FileName ?? "front.jpg");
        }
        if (backImage is { Length: > 0 })
        {
            var content = new StreamContent(backImage.OpenReadStream());
            content.Headers.ContentType = new MediaTypeHeaderValue(backImage.ContentType ?? "application/octet-stream");
            form.Add(content, "backImage", backImage.FileName ?? "back.jpg");
        }

        var response = await SendMultipartAsync(HttpMethod.Post, $"api/v1/users/{userId}/identity-documents", form, cancellationToken);
        return await ReadJsonAsync<IdentityDocumentListItemDto>(response, cancellationToken);
    }

    public async Task<ApiResult<IdentityDocumentListItemDto>> UpdateDocumentAsync(
        Guid userId, Guid documentId, string documentNumber,
        DateOnly? issuedDate, DateOnly? expiryDate,
        IFormFile? frontImage, IFormFile? backImage,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(documentNumber), "documentNumber");
        if (issuedDate is { } i) form.Add(new StringContent(i.ToString("yyyy-MM-dd")), "issuedDate");
        if (expiryDate is { } e) form.Add(new StringContent(e.ToString("yyyy-MM-dd")), "expiryDate");

        if (frontImage is { Length: > 0 })
        {
            var content = new StreamContent(frontImage.OpenReadStream());
            content.Headers.ContentType = new MediaTypeHeaderValue(frontImage.ContentType ?? "application/octet-stream");
            form.Add(content, "frontImage", frontImage.FileName ?? "front.jpg");
        }
        if (backImage is { Length: > 0 })
        {
            var content = new StreamContent(backImage.OpenReadStream());
            content.Headers.ContentType = new MediaTypeHeaderValue(backImage.ContentType ?? "application/octet-stream");
            form.Add(content, "backImage", backImage.FileName ?? "back.jpg");
        }

        var response = await SendMultipartAsync(HttpMethod.Put, $"api/v1/users/{userId}/identity-documents/{documentId}", form, cancellationToken);
        return await ReadJsonAsync<IdentityDocumentListItemDto>(response, cancellationToken);
    }

    public async Task<ApiResult> DeleteDocumentAsync(Guid userId, Guid documentId, CancellationToken cancellationToken = default)
    {
        var response = await SendNoBodyAsync(HttpMethod.Delete, $"api/v1/users/{userId}/identity-documents/{documentId}", cancellationToken);
        return await ReadNoBodyAsync(response);
    }

    public async Task<HttpResponseMessage?> GetDocumentImageAsync(
        Guid userId, Guid documentId, string side,
        EntityTagHeaderValue? ifNoneMatch, CancellationToken cancellationToken = default)
    {
        var url = $"api/v1/users/{userId}/identity-documents/{documentId}/images/{side}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (ifNoneMatch is not null)
        {
            request.Headers.IfNoneMatch.Add(ifNoneMatch);
        }
        try
        {
            var response = await _client.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                return null;
            }
            // Detach the response from the HttpClient lifetime so the controller can stream it.
            return new HttpResponseMessage(response.StatusCode)
            {
                Content = new StreamContent(await response.Content.ReadAsStreamAsync(cancellationToken)),
                Version = response.Version
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Transport error fetching identity document image for {UserId}/{DocumentId}.", userId, documentId);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Timeout fetching identity document image for {UserId}/{DocumentId}.", userId, documentId);
            return null;
        }
    }

    // ============== OCR ==============

    public async Task<OcrScanResponseDto?> ScanOcrAsync(
        Guid userId, string documentType,
        IFormFile? frontImage, IFormFile? backImage,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(documentType), "documentType");

        if (frontImage is { Length: > 0 })
        {
            var content = new StreamContent(frontImage.OpenReadStream());
            content.Headers.ContentType = new MediaTypeHeaderValue(frontImage.ContentType ?? "application/octet-stream");
            form.Add(content, "frontImage", frontImage.FileName ?? "front.jpg");
        }
        if (backImage is { Length: > 0 })
        {
            var content = new StreamContent(backImage.OpenReadStream());
            content.Headers.ContentType = new MediaTypeHeaderValue(backImage.ContentType ?? "application/octet-stream");
            form.Add(content, "backImage", backImage.FileName ?? "back.jpg");
        }

        HttpResponseMessage response;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"api/v1/users/{userId}/identity-documents/ocr/scan")
            {
                Content = form
            };
            response = await _client.HttpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Transport error during OCR scan for user {UserId}.", userId);
            throw new ApiTransportException("Không thể kết nối tới máy chủ API.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var problem = await TryReadProblemAsync(response, cancellationToken);
                _logger.LogWarning("OCR scan failed with status {Status}: {Title}", (int)response.StatusCode, problem?.Title);
                return null;
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<OcrScanResponseDto>(JsonOpts, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse the OCR scan response.");
                return null;
            }
        }
    }

    public async Task<ApiResult<IdentityDocumentListItemDto>> ConfirmOcrAsync(
        Guid userId, OcrConfirmRequest request, CancellationToken cancellationToken = default)
    {
        var response = await SendJsonAsync(HttpMethod.Post, $"api/v1/users/{userId}/identity-documents/ocr/confirm", request, cancellationToken);
        return await ReadJsonAsync<IdentityDocumentListItemDto>(response, cancellationToken);
    }

    // ============== internals ==============

    private async Task<ApiResult<T>> GetJsonAsync<T>(string url, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.HttpClient.GetAsync(url, cancellationToken);
            return await ReadJsonAsync<T>(response, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Transport error during GET {Url}", url);
            throw new ApiTransportException("Không thể kết nối tới máy chủ API.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendJsonAsync<T>(HttpMethod method, string url, T body, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(body, JsonOpts);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(method, url) { Content = content };
            return await _client.HttpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Transport error during {Method} {Url}", method, url);
            throw new ApiTransportException("Không thể kết nối tới máy chủ API.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendMultipartAsync(HttpMethod method, string url, HttpContent content, CancellationToken cancellationToken)
    {
        try
        {
            var request = new HttpRequestMessage(method, url) { Content = content };
            return await _client.HttpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Transport error during {Method} {Url}", method, url);
            throw new ApiTransportException("Không thể kết nối tới máy chủ API.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendNoBodyAsync(HttpMethod method, string url, CancellationToken cancellationToken)
    {
        try
        {
            var request = new HttpRequestMessage(method, url);
            return await _client.HttpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Transport error during {Method} {Url}", method, url);
            throw new ApiTransportException("Không thể kết nối tới máy chủ API.", ex);
        }
    }

    private async Task<ApiResult<T>> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            try
            {
                var value = await response.Content.ReadFromJsonAsync<T>(JsonOpts, cancellationToken);
                if (value is null)
                {
                    return ApiResult<T>.Failure((int)response.StatusCode,
                        new ProblemDetailsDto(null, "Phản hồi rỗng từ máy chủ.", (int)response.StatusCode, null, null, null, null));
                }
                return ApiResult<T>.Ok(value, (int)response.StatusCode);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse API response for {Status}", response.StatusCode);
                return ApiResult<T>.Failure((int)response.StatusCode,
                    new ProblemDetailsDto(null, "Phản hồi từ máy chủ không hợp lệ.", (int)response.StatusCode, ex.Message, null, null, null));
            }
        }

        var problem = await TryReadProblemAsync(response, cancellationToken);
        return ApiResult<T>.Failure((int)response.StatusCode, problem);
    }

    private async Task<ApiResult> ReadNoBodyAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return ApiResult.Ok((int)response.StatusCode);
        }
        var problem = await TryReadProblemAsync(response, CancellationToken.None);
        return ApiResult.Failure((int)response.StatusCode, problem);
    }

    private async Task<ProblemDetailsDto?> TryReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            // Problem Details can be either application/problem+json or application/json.
            if (response.Content.Headers.ContentType?.MediaType is "application/problem+json" or "application/json")
            {
                return await response.Content.ReadFromJsonAsync<ProblemDetailsDto>(JsonOpts, cancellationToken);
            }
            // Otherwise, fall back to a synthesized ProblemDetails from the body.
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(raw)
                ? null
                : new ProblemDetailsDto(null, null, (int)response.StatusCode, raw, null, null, null);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Problem Details body.");
            return null;
        }
    }
}
