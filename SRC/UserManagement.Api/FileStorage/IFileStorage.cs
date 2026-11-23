namespace UserManagement.Api.FileStorage;

/// <summary>
/// Result of a successful upload: relative path under the storage root and the detected
/// content type (so the API can return correct Content-Type without trusting the client).
/// </summary>
public sealed record StoredFile(string RelativePath, string ContentType, long Size, DateTimeOffset LastModifiedUtc);

/// <summary>
/// Abstraction over the physical file system. The implementation writes images outside
/// <c>wwwroot</c> using server-generated filenames and never exposes the absolute path
/// to API consumers.
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Root path where files are stored. All relative paths returned by other methods are
    /// relative to this root.
    /// </summary>
    string RootPath { get; }

    /// <summary>
    /// Writes <paramref name="source"/> under <c>identity-documents/{userId}/{fileName}</c>
    /// and returns the relative path. Throws if the source stream cannot be read.
    /// </summary>
    Task<StoredFile> SaveIdentityDocumentImageAsync(
        Guid userId,
        Guid documentId,
        string side,
        Stream source,
        string contentType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Opens a stored file for reading. Returns <c>null</c> if the file does not exist.
    /// </summary>
    StoredFileHandle? OpenForRead(string relativePath);

    /// <summary>
    /// Deletes a file by its relative path. Returns <c>true</c> if it was removed,
    /// <c>false</c> if it didn't exist.
    /// </summary>
    bool TryDelete(string relativePath);
}

/// <summary>
/// Lightweight handle to an opened file. Disposing it closes the underlying stream.
/// </summary>
public sealed class StoredFileHandle : IDisposable
{
    public StoredFileHandle(Stream stream, string contentType, long size, DateTimeOffset lastModifiedUtc)
    {
        Stream = stream;
        ContentType = contentType;
        Size = size;
        LastModifiedUtc = lastModifiedUtc;
    }

    public Stream Stream { get; }
    public string ContentType { get; }
    public long Size { get; }
    public DateTimeOffset LastModifiedUtc { get; }

    public void Dispose() => Stream.Dispose();
}
