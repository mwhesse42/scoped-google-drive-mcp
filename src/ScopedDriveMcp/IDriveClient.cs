namespace ScopedDriveMcp;

/// <summary>A Drive file or folder. Id is null only for the virtual "/" that holds everything shared with the account.</summary>
public sealed record DriveFile(string? Id, string Name, string MimeType, long? Size = null, DateTimeOffset? Modified = null);

public sealed record DriveFilePage(IReadOnlyList<DriveFile> Files, string? NextPageToken = null);

/// <summary>
/// The three Google Drive calls the server makes. Kept narrow so DriveWorkspace's rules
/// can be tested against a mock instead of the real Google API.
/// </summary>
public interface IDriveClient
{
    Task<DriveFilePage> ListAsync(string query, string? pageToken, int pageSize, CancellationToken ct);

    /// <summary>Downloads a file's bytes, or exports a Google-native file when exportMimeType is set.</summary>
    Task<byte[]> DownloadAsync(string fileId, string? exportMimeType, CancellationToken ct);

    Task UploadAsync(string fileId, byte[] content, string mimeType, CancellationToken ct);
}
