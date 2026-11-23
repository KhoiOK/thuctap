using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace UserManagement.Api.FileStorage;

/// <summary>
/// Local-disk implementation of <see cref="IFileStorage"/>. Files live under
/// <c>{RootPath}/identity-documents/{userId}/{documentId}-{side}-{guid}.{ext}</c> so the
/// folder structure mirrors Database-Design §6.2.
/// </summary>
public class LocalFileStorage : IFileStorage
{
    private const string Subfolder = "identity-documents";

    public LocalFileStorage(IOptions<FileStorageOptions> options)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.RootPath))
        {
            throw new InvalidOperationException("FileStorage:RootPath must be configured.");
        }

        RootPath = Path.GetFullPath(opts.RootPath);
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public async Task<StoredFile> SaveIdentityDocumentImageAsync(
        Guid userId,
        Guid documentId,
        string side,
        Stream source,
        string contentType,
        CancellationToken cancellationToken)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (string.IsNullOrWhiteSpace(side)) throw new ArgumentException("Side is required.", nameof(side));
        if (side is not ("front" or "back"))
        {
            throw new ArgumentException("Side must be 'front' or 'back'.", nameof(side));
        }

        var ext = contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            _ => throw new ArgumentException($"Unsupported content type '{contentType}'.", nameof(contentType))
        };

        var guid = CreateRandomFileId();
        var fileName = $"{documentId}-{side}-{guid}{ext}";
        var userDir = Path.Combine(RootPath, Subfolder, userId.ToString());
        Directory.CreateDirectory(userDir);
        var fullPath = Path.Combine(userDir, fileName);

        // Stream-copy so we never load the whole file into memory.
        await using (var dest = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await source.CopyToAsync(dest, cancellationToken);
        }

        var info = new FileInfo(fullPath);
        var relative = Path.Combine(Subfolder, userId.ToString(), fileName)
            .Replace('\\', '/');

        return new StoredFile(relative, contentType, info.Length, info.LastWriteTimeUtc);
    }

    public StoredFileHandle? OpenForRead(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var fullPath = ResolveSafePath(relativePath);
        if (fullPath is null || !File.Exists(fullPath))
        {
            return null;
        }

        var info = new FileInfo(fullPath);
        var contentType = info.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg";

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return new StoredFileHandle(stream, contentType, info.Length, info.LastWriteTimeUtc);
    }

    public bool TryDelete(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var fullPath = ResolveSafePath(relativePath);
        if (fullPath is null || !File.Exists(fullPath))
        {
            return false;
        }

        File.Delete(fullPath);

        // Best-effort cleanup of empty user directories.
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir)
            && !string.Equals(dir, RootPath, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(dir)
            && !Directory.EnumerateFileSystemEntries(dir).Any())
        {
            try { Directory.Delete(dir); } catch { /* ignore */ }
        }

        return true;
    }

    /// <summary>
    /// Resolves a relative path to a full path while making sure the result stays under
    /// <see cref="RootPath"/>. Returns <c>null</c> if the path would escape the root.
    /// </summary>
    private string? ResolveSafePath(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar)
                                    .Replace('\\', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(RootPath, normalized));
        var rootWithSep = RootPath.EndsWith(Path.DirectorySeparatorChar)
            ? RootPath
            : RootPath + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return fullPath;
    }

    private static string CreateRandomFileId()
    {
        Span<byte> buffer = stackalloc byte[16];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToHexString(buffer).ToLowerInvariant();
    }
}
