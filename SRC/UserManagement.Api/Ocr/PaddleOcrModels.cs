using System.Text.Json.Serialization;

namespace UserManagement.Api.Ocr;

/// <summary>
/// Raw response of the PaddleOCR service (<c>POST /ocr/identity-document</c>).
/// See <c>OCR-Cloudinary-Spec.md</c> §3.2.1.
/// </summary>
public sealed class PaddleOcrResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("engine")]
    public string? Engine { get; set; }

    [JsonPropertyName("documentType")]
    public string? DocumentType { get; set; }

    [JsonPropertyName("fields")]
    public Dictionary<string, PaddleOcrField>? Fields { get; set; }

    [JsonPropertyName("rawText")]
    public Dictionary<string, string>? RawText { get; set; }

    [JsonPropertyName("processingTimeMs")]
    public int ProcessingTimeMs { get; set; }

    [JsonPropertyName("warnings")]
    public List<string>? Warnings { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>One field as returned by the OCR engine, before normalization.</summary>
public sealed class PaddleOcrField
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("boundingBox")]
    public double[]? BoundingBox { get; set; }
}
