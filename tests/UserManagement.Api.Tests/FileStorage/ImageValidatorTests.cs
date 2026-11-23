using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using UserManagement.Api.FileStorage;
using Xunit;

namespace UserManagement.Api.Tests.FileStorage;

/// <summary>
/// Unit tests for <see cref="ImageValidator"/>. Uses ImageSharp itself to generate the
/// test images so the magic bytes and dimensions are guaranteed correct.
/// </summary>
public class ImageValidatorTests
{
    private static ImageValidator CreateValidator(long maxBytes = 5_000_000, int maxPixels = 8000)
    {
        var opts = Options.Create(new FileStorageOptions
        {
            RootPath = Path.GetTempPath(),
            MaxFileSizeBytes = maxBytes,
            MaxPixelSize = maxPixels
        });
        return new ImageValidator(opts, NullLogger<ImageValidator>.Instance);
    }

    private static byte[] CreateJpeg(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(255, 0, 0));
        using var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder { Quality = 80 });
        return ms.ToArray();
    }

    private static byte[] CreatePng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(0, 0, 255));
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    [Fact]
    public async Task Valid_Jpeg_Passes()
    {
        var validator = CreateValidator();
        using var ms = new MemoryStream(CreateJpeg(640, 480));
        var result = await validator.ValidateAsync(ms, "image/jpeg", CancellationToken.None);
        if (!result.IsValid)
        {
            throw new Xunit.Sdk.XunitException($"JPEG validation failed: {result.Error} (status {result.StatusCode})");
        }
        result.IsValid.Should().BeTrue();
        result.ContentType.Should().Be("image/jpeg");
        result.Image!.Width.Should().Be(640);
        result.Image.Height.Should().Be(480);
    }

    [Fact]
    public async Task Valid_Png_Passes()
    {
        var validator = CreateValidator();
        using var ms = new MemoryStream(CreatePng(100, 100));
        var result = await validator.ValidateAsync(ms, "image/png", CancellationToken.None);
        result.IsValid.Should().BeTrue();
        result.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task Oversize_File_Fails_With_413()
    {
        var validator = CreateValidator(maxBytes: 1000);
        using var ms = new MemoryStream(CreateJpeg(640, 480));
        var result = await validator.ValidateAsync(ms, "image/jpeg", CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.StatusCode.Should().Be(413);
    }

    [Fact]
    public async Task Bogus_Bytes_Fails_With_415()
    {
        var validator = CreateValidator();
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };
        using var ms = new MemoryStream(bytes);
        var result = await validator.ValidateAsync(ms, "image/jpeg", CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.StatusCode.Should().Be(415);
    }

    [Fact]
    public async Task Truncated_Jpeg_Fails()
    {
        var validator = CreateValidator();
        var fullBytes = CreateJpeg(640, 480);
        // Take only first 100 bytes - well before EOI marker.
        using var ms = new MemoryStream(fullBytes[..100]);
        var result = await validator.ValidateAsync(ms, "image/jpeg", CancellationToken.None);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Image_Exceeding_Max_Pixels_Fails_With_400()
    {
        var validator = CreateValidator(maxPixels: 1000);
        using var ms = new MemoryStream(CreateJpeg(1200, 900));
        var result = await validator.ValidateAsync(ms, "image/jpeg", CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("Kích thước ảnh");
    }

    [Fact]
    public async Task Stream_Position_Is_Rewound_After_Validation()
    {
        var validator = CreateValidator();
        using var ms = new MemoryStream(CreateJpeg(640, 480));
        await validator.ValidateAsync(ms, "image/jpeg", CancellationToken.None);
        ms.Position.Should().Be(0, "caller may want to re-read the stream after validation");
    }
}
