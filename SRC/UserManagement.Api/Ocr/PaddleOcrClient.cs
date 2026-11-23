using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace UserManagement.Api.Ocr;

/// <summary>
/// Raised when the OCR engine is unreachable, times out, or answers with an error.
/// The API layer turns this into HTTP 503 so the user can still enter the data manually
/// (OCR is optional, never a precondition for saving).
/// </summary>
public class OcrUnavailableException : Exception
{
    public OcrUnavailableException(string message) : base(message) { }
    public OcrUnavailableException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Raised when the OCR engine ran but could not read anything useful from the images.
/// The API layer turns this into HTTP 400.
/// </summary>
public class OcrUnreadableException : Exception
{
    public OcrUnreadableException(string message) : base(message) { }
}

/// <summary>
/// HTTP client for the PaddleOCR service. Speaks the REST contract described in
/// <c>OCR-Cloudinary-Spec.md</c> §3.2.
/// </summary>
public interface IPaddleOcrClient
{
    /// <summary>
    /// Sends the front (and optionally back) image to the OCR engine and returns the raw
    /// response. Throws <see cref="OcrUnavailableException"/> when the service cannot be
    /// reached and <see cref="OcrUnreadableException"/> when nothing could be read.
    /// </summary>
    Task<PaddleOcrResponse> RecognizeAsync(
        Stream frontImage,
        Stream backImage,
        string documentType,
        CancellationToken cancellationToken);

    /// <summary>Checks whether the OCR service is up.</summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken);
}

public class PaddleOcrClient : IPaddleOcrClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly OcrOptions _options;
    private readonly ILogger<PaddleOcrClient> _logger;

    public PaddleOcrClient(
        HttpClient http,
        Microsoft.Extensions.Options.IOptions<OcrOptions> options,
        ILogger<PaddleOcrClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PaddleOcrResponse> RecognizeAsync(
        Stream frontImage,
        Stream backImage,
        string documentType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new OcrUnavailableException("OCR:BaseUrl chưa được cấu hình.");
        }

        if (frontImage.CanSeek) frontImage.Position = 0;
        if (backImage.CanSeek) backImage.Position = 0;

        using var form = new MultipartFormDataContent();

        var frontContent = new StreamContent(frontImage);
        frontContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(frontContent, "front", "front.jpg");

        if (backImage is not null)
        {
            var backContent = new StreamContent(backImage);
            backContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(backContent, "back", "back.jpg");
        }

        form.Add(new StringContent(documentType), "documentType");

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync($"{_options.BaseUrl.TrimEnd('/')}/ocr/identity-document", form, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OcrUnavailableException("Dịch vụ quét tự động không phản hồi trong thời gian cho phép.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new OcrUnavailableException("Không thể kết nối tới dịch vụ quét tự động.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest)
            {
                var message = TryReadError(body) ?? "Không nhận dạng được vùng chữ trên ảnh giấy tờ.";
                throw new OcrUnreadableException(message);
            }

            if ((int)response.StatusCode >= 500)
            {
                throw new OcrUnavailableException("Dịch vụ quét tự động đang gặp sự cố.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new OcrUnavailableException($"Dịch vụ quét tự động trả về mã {(int)response.StatusCode}.");
            }

            PaddleOcrResponse? payload;
            try
            {
                payload = JsonSerializer.Deserialize<PaddleOcrResponse>(body, JsonOpts);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "OCR service returned a malformed payload.");
                throw new OcrUnavailableException("Dịch vụ quét tự động trả về dữ liệu không hợp lệ.", ex);
            }

            if (payload is null)
            {
                throw new OcrUnavailableException("Dịch vụ quét tự động trả về phản hồi rỗng.");
            }

            if (!payload.Success)
            {
                throw new OcrUnreadableException(
                    payload.Error ?? "Không nhận dạng được thông tin trên ảnh giấy tờ.");
            }

            return payload;
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return false;
        }

        try
        {
            using var response = await _http.GetAsync(
                $"{_options.BaseUrl.TrimEnd('/')}/health",
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private static string? TryReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                return error.GetString();
            }
            if (doc.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
            {
                return detail.GetString();
            }
        }
        catch (JsonException)
        {
            // Not JSON - fall through and return null.
        }

        return null;
    }
}
