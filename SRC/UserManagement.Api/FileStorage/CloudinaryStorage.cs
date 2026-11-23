using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace UserManagement.Api.FileStorage;

/// <summary>
/// Cloudinary implementation of <see cref="ICloudinaryStorage"/> built on raw HTTP
/// (<c>HttpClient</c> + SHA-1 request signing) instead of the vendor SDK. This keeps the
/// dependency footprint small, gives exact control over the multipart payload and makes
/// the class easy to test with a mocked <c>HttpMessageHandler</c>.
/// </summary>
/// <remarks>
/// Reference: <c>OCR-Cloudinary-Spec.md</c> §4.
/// </remarks>
public class CloudinaryStorage : ICloudinaryStorage
{
    private readonly HttpClient _http;
    private readonly CloudinaryOptions _options;
    private readonly ILogger<CloudinaryStorage> _logger;

    public CloudinaryStorage(
        HttpClient http,
        IOptions<CloudinaryOptions> options,
        ILogger<CloudinaryStorage> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.CloudName)
        && !string.IsNullOrWhiteSpace(_options.ApiKey)
        && !string.IsNullOrWhiteSpace(_options.ApiSecret);

    /// <summary>
    /// Whether assets are stored with Cloudinary's <c>authenticated</c> type, i.e. they
    /// are only reachable through a signed delivery URL.
    /// </summary>
    private bool SecureDelivery => _options.UseSecureDelivery;

    // =================== Upload ===================

    public async Task<CloudinaryUploadResult> UploadAsync(
        string folder,
        string fileNamePrefix,
        Stream source,
        string contentType,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (source is null) throw new ArgumentNullException(nameof(source));

        if (source.CanSeek)
        {
            source.Position = 0;
        }

        var extension = ResolveExtension(contentType);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var targetFolder = BuildFolderPath(folder);

        // The asset name is explicit so the public id stays meaningful (and keeps the
        // side prefix) instead of being a random hash. It deliberately carries NO
        // extension: Cloudinary stores the format separately, and a public id that
        // already ends in ".png" would need a doubled extension in the delivery URL.
        var assetName = $"{fileNamePrefix}-{Guid.NewGuid():N}";

        // Cloudinary signs EVERY parameter sent in the body except `file`, `cloud_name`,
        // `resource_type` and `api_key`, and it verifies the exact `name=value` pairs.
        // So the dictionary below is the single source of truth: the same entries are
        // hashed and the same entries are posted. Keeping them apart caused an
        // "Invalid Signature" error (allowed_formats was duplicated in the form).
        var signedParams = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["allowed_formats"] = string.Join(",", _options.AllowedFormats),
            ["folder"] = targetFolder,
            ["public_id"] = assetName,
            ["timestamp"] = timestamp,
            ["use_filename"] = "false",
            ["unique_filename"] = "false"
        };
        if (SecureDelivery)
        {
            // `type=authenticated` is what actually protects the asset. `access_mode`
            // is ignored by the API and would leave the image publicly readable.
            // Every later call (rename, destroy) must repeat it, otherwise Cloudinary
            // looks for an asset of type `upload` and does not find it.
            signedParams["type"] = "authenticated";
        }

        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(source);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", $"{assetName}{extension}");

        form.Add(new StringContent(_options.ApiKey), "api_key");
        form.Add(new StringContent(ComputeSignature(signedParams)), "signature");
        foreach (var (key, value) in signedParams)
        {
            form.Add(new StringContent(value), key);
        }

        using var response = await _http.PostAsync(BuildApiUrl("image/upload"), form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Cloudinary returns a JSON body like {"error":{"message":"Invalid cloud_name cccd"}}.
            // Surfacing that message is the difference between "401" and a fixable config error.
            var detail = ExtractErrorMessage(body);
            _logger.LogError("Cloudinary upload failed with status {Status}: {Detail}", (int)response.StatusCode, detail);
            throw new CloudinaryException(BuildMessage(
                "upload", (int)response.StatusCode, detail, _options.CloudName));
        }

        var payload = System.Text.Json.JsonSerializer.Deserialize<CloudinaryUploadResponse>(body)
            ?? throw new CloudinaryException("Cloudinary returned an empty upload response.");

        if (string.IsNullOrWhiteSpace(payload.PublicId))
        {
            throw new CloudinaryException("Cloudinary did not return a public id for the uploaded asset.");
        }

        return new CloudinaryUploadResult(
            payload.PublicId,
            payload.Version,
            ResolveContentType(payload.Format),
            payload.Bytes,
            payload.Width,
            payload.Height);
    }

    // =================== Move ===================

    public async Task<CloudinaryUploadResult> MoveAsync(
        string sourcePublicId,
        string targetFolder,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(sourcePublicId))
        {
            throw new ArgumentException("Source public id is required.", nameof(sourcePublicId));
        }

        var targetFolderPath = BuildFolderPath(targetFolder);
        var targetPublicId = $"{targetFolderPath}/{Path.GetFileName(sourcePublicId)}";

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        // Same rule as upload: the signed set and the posted set must be identical.
        var signedParams = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["from_public_id"] = sourcePublicId,
            ["to_public_id"] = targetPublicId,
            ["overwrite"] = "false",
            ["timestamp"] = timestamp
        };
        if (_options.AutoInvalidate)
        {
            signedParams["invalidate"] = "true";
        }
        if (SecureDelivery)
        {
            // The source asset lives under type `authenticated`; without repeating the
            // type Cloudinary looks for an `upload` asset and answers "Resource not found".
            signedParams["type"] = "authenticated";
        }

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(_options.ApiKey), "api_key");
        form.Add(new StringContent(ComputeSignature(signedParams)), "signature");
        foreach (var (key, value) in signedParams)
        {
            form.Add(new StringContent(value), key);
        }

        using var response = await _http.PostAsync(BuildApiUrl("image/rename"), form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var detail = ExtractErrorMessage(body);
            _logger.LogError("Cloudinary rename failed with status {Status}: {Detail}", (int)response.StatusCode, detail);
            throw new CloudinaryException(BuildMessage(
                "rename", (int)response.StatusCode, detail, _options.CloudName));
        }

        var payload = System.Text.Json.JsonSerializer.Deserialize<CloudinaryUploadResponse>(body)
            ?? throw new CloudinaryException("Cloudinary returned an empty rename response.");

        return new CloudinaryUploadResult(
            string.IsNullOrWhiteSpace(payload.PublicId) ? targetPublicId : payload.PublicId,
            payload.Version,
            ResolveContentType(payload.Format),
            payload.Bytes,
            payload.Width,
            payload.Height);
    }

    // =================== Delete ===================

    public async Task<IReadOnlyList<string>> DeleteAsync(
        IEnumerable<string> publicIds,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var failed = new List<string>();

        foreach (var publicId in publicIds.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct())
        {
            try
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

                // Same rule as upload: signed set == posted set.
                var signedParams = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["public_id"] = publicId,
                    ["timestamp"] = timestamp
                };
                if (_options.AutoInvalidate)
                {
                    signedParams["invalidate"] = "true";
                }
                if (SecureDelivery)
                {
                    // Without the matching type the destroy silently reports "not found"
                    // and the image stays on the CDN forever.
                    signedParams["type"] = "authenticated";
                }

                using var form = new MultipartFormDataContent();
                form.Add(new StringContent(_options.ApiKey), "api_key");
                form.Add(new StringContent(ComputeSignature(signedParams)), "signature");
                foreach (var (key, value) in signedParams)
                {
                    form.Add(new StringContent(value), key);
                }

                using var response = await _http.PostAsync(BuildApiUrl("image/destroy"), form, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    failed.Add(publicId);
                    var detail = ExtractErrorMessage(await response.Content.ReadAsStringAsync(cancellationToken));
                    _logger.LogWarning(
                        "Cloudinary destroy for {PublicId} returned {Status}: {Detail}",
                        publicId, (int)response.StatusCode, detail);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                failed.Add(publicId);
                _logger.LogWarning(ex, "Failed to delete Cloudinary asset {PublicId}.", publicId);
            }
        }

        return failed;
    }

    // =================== Read ===================

    public string BuildDeliveryUrl(string publicId)
    {
        if (string.IsNullOrWhiteSpace(publicId))
        {
            return string.Empty;
        }

        if (!SecureDelivery)
        {
            return $"https://res.cloudinary.com/{_options.CloudName}/image/upload/{publicId}";
        }

        // `image/authenticated` assets are NOT publicly readable: Cloudinary answers 401
        // for an unsigned URL. The URL therefore has to carry a signature computed from
        // the public id and the API secret.
        var signature = ComputeDeliverySignature(publicId);
        return $"https://res.cloudinary.com/{_options.CloudName}/image/authenticated/s--{signature}--/{publicId}";
    }

    /// <summary>
    /// Computes the signature embedded in a delivery URL.
    /// </summary>
    /// <remarks>
    /// Cloudinary's delivery URL signature is the base64url encoded SHA-1 digest of
    /// the public id concatenated with the API secret, truncated to the first 8
    /// characters (<c>s--{signature}--</c>). The API secret is appended with no
    /// delimiter, exactly like the API request signature.
    /// </remarks>
    internal static string ComputeDeliverySignature(string publicId, string apiSecret)
    {
        var toHash = publicId + apiSecret;
        var digest = SHA1.HashData(Encoding.UTF8.GetBytes(toHash));

        // Cloudinary uses the URL-safe base64 alphabet and keeps the padding.
        var encoded = Convert.ToBase64String(digest)
            .Replace('+', '-')
            .Replace('/', '_');

        return encoded[..8];
    }

    private string ComputeDeliverySignature(string publicId)
        => ComputeDeliverySignature(publicId, _options.ApiSecret);

    public async Task<CloudinaryDownloadResult?> DownloadAsync(string publicId, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(publicId))
        {
            return null;
        }

        using var response = await _http.GetAsync(BuildDeliveryUrl(publicId), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Cloudinary download for {PublicId} returned {Status}.", publicId, (int)response.StatusCode);
            return null;
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        var length = response.Content.Headers.ContentLength ?? -1L;
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        return new CloudinaryDownloadResult(stream, contentType, length, 0);
    }

    // =================== helpers ===================

    /// <summary>
    /// Extracts the human-readable message from a Cloudinary error payload
    /// (<c>{"error":{"message":"..."}}</c>). Returns an empty string when the body is
    /// not a recognisable Cloudinary error, so callers can always append it safely.
    /// </summary>
    internal static string ExtractErrorMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == System.Text.Json.JsonValueKind.Object
                    && error.TryGetProperty("message", out var message)
                    && message.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return message.GetString() ?? string.Empty;
                }
                if (error.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return error.GetString() ?? string.Empty;
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Not JSON (e.g. Cloudinary's HTML 404 page) - fall through.
        }

        return string.Empty;
    }

    /// <summary>
    /// Computes the Cloudinary request signature.
    /// </summary>
    /// <remarks>
    /// Cloudinary's rule (https://cloudinary.com/documentation/authentication_signatures):
    /// take every parameter sent in the POST body except <c>file</c>, <c>cloud_name</c>,
    /// <c>resource_type</c> and <c>api_key</c>, include <c>timestamp</c>, build
    /// <c>name=value</c> pairs sorted alphabetically, join them with <c>&amp;</c>, append
    /// the API secret with no delimiter, then hash.
    ///
    /// Signature version 2 (the Cloudinary default) escapes only the ampersand inside a
    /// value (<c>&amp;</c> to <c>%26</c>) to prevent parameter smuggling. Nothing else is
    /// escaped - in particular <c>,</c> and <c>/</c> stay literal.
    /// </remarks>
    internal static string ComputeSignature(IReadOnlyDictionary<string, string> parameters, string apiSecret)
    {
        var payload = string.Join("&", parameters
            .Where(p => !string.IsNullOrEmpty(p.Value))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={p.Value.Replace("&", "%26", StringComparison.Ordinal)}"));

        var toHash = payload + apiSecret;
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(toHash));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string ComputeSignature(IReadOnlyDictionary<string, string> parameters)
        => ComputeSignature(parameters, _options.ApiSecret);

    private string BuildFolderPath(string folder)
    {
        var root = (_options.Folder ?? string.Empty).Trim('/');
        var child = (folder ?? string.Empty).Trim('/');

        if (string.IsNullOrEmpty(child))
        {
            return root;
        }
        if (string.IsNullOrEmpty(root))
        {
            return child;
        }
        return $"{root}/{child}";
    }

    private string BuildApiUrl(string endpoint)
        => $"https://api.cloudinary.com/v1_1/{_options.CloudName}/{endpoint}";

    /// <summary>
    /// Builds a full error message: what failed, the status code, the detail Cloudinary
    /// returned and - when the detail points at a config problem - a concrete hint.
    /// </summary>
    private static string BuildMessage(string operation, int statusCode, string detail, string cloudName)
    {
        var message = $"Cloudinary {operation} failed with status {statusCode}.";
        if (!string.IsNullOrWhiteSpace(detail))
        {
            message += $" {detail}";
        }

        var hint = BuildHint(detail, cloudName);
        if (!string.IsNullOrWhiteSpace(hint))
        {
            message += $" {hint}";
        }

        return message;
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new CloudinaryException(
                "Cloudinary chưa được cấu hình. Thiết lập Cloudinary:CloudName, Cloudinary:ApiKey và Cloudinary:ApiSecret.");
        }
    }

    /// <summary>
    /// Turns Cloudinary's terse 401 messages into an actionable hint. The most common
    /// cause of a 401 that is NOT a bad secret is a wrong <c>CloudName</c>: Cloudinary
    /// validates the api_key/secret pair first and then reports "Invalid cloud_name"
    /// or "cloud_name mismatch" when the key belongs to a different product environment.
    /// </summary>
    internal static string BuildHint(string detail, string cloudName)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return string.Empty;
        }

        if (detail.Contains("cloud_name", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("mismatch", StringComparison.OrdinalIgnoreCase))
        {
            return $"Kiểm tra lại Cloudinary:CloudName (hiện tại: '{cloudName}'). "
                 + "Cloud name nằm trên trang API Keys của Cloudinary Console và phải đi kèm đúng api_key.";
        }

        if (detail.Contains("Invalid Signature", StringComparison.OrdinalIgnoreCase))
        {
            return "Chữ ký không hợp lệ - kiểm tra lại Cloudinary:ApiSecret.";
        }

        return string.Empty;
    }

    private static string ResolveExtension(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        _ => ".jpg"
    };

    private static string ResolveContentType(string? format) => format?.ToUpperInvariant() switch
    {
        "PNG" => "image/png",
        _ => "image/jpeg"
    };

    private sealed class CloudinaryUploadResponse
    {
        // Cloudinary uses snake_case in its JSON payloads.
        [System.Text.Json.Serialization.JsonPropertyName("public_id")]
        public string? PublicId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("version")]
        public long Version { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("format")]
        public string? Format { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("bytes")]
        public long Bytes { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("width")]
        public int Width { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("height")]
        public int Height { get; set; }
    }
}

/// <summary>Raised when a Cloudinary API call fails.</summary>
public class CloudinaryException : Exception
{
    public CloudinaryException(string message) : base(message) { }
    public CloudinaryException(string message, Exception inner) : base(message, inner) { }
}
