using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;

namespace UserManagement.Web.Tests.Infrastructure;

/// <summary>
/// Helpers to produce real image files on disk so a browser can upload them
/// through a file input (Playwright requires a real path, not a byte array).
/// </summary>
internal static class TestImages
{
    /// <summary>Writes a valid JPEG (default 640x480) and returns the file path.</summary>
    public static string WriteJpeg(string directory, string name, int width = 640, int height = 480,
        byte r = 200, byte g = 30, byte b = 30)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        using var image = new Image<Rgba32>(width, height, new Rgba32(r, g, b));
        image.Save(path, new JpegEncoder { Quality = 80 });
        return path;
    }

    /// <summary>Writes a valid PNG and returns the file path.</summary>
    public static string WritePng(string directory, string name, int width = 640, int height = 480)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        using var image = new Image<Rgba32>(width, height, new Rgba32(20, 120, 200));
        image.Save(path, new PngEncoder());
        return path;
    }

    /// <summary>
    /// Writes a file that is NOT a valid image (plain text with a .jpg extension)
    /// so the server-side image validation can be exercised.
    /// </summary>
    public static string WriteInvalidImage(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, "This is not an image. Just plain text pretending to be a JPG.");
        return path;
    }
}
