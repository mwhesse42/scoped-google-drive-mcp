using System.Text;

namespace ScopedDriveMcp;

public sealed record DriveEntry(string Path, bool IsFolder, string MimeType, long? Size, DateTimeOffset? Modified);

/// <summary>
/// Google Drive access limited to what has been shared with the signed-in account.
/// "/" is either everything shared with it, or the configured RootFolderId. Paths look like "/Projects/notes".
/// </summary>
public sealed class DriveWorkspace(IDriveClient client, DriveSettings settings)
{
    internal const int ListPageSize = 1000;

    // Large enough to see duplicates; a lookup only ever needs one match.
    internal const int LookupPageSize = 10;

    public async Task<IReadOnlyList<DriveEntry>> ListAsync(string path, CancellationToken ct)
    {
        var folderPath = WorkspacePath.Normalize(path);
        var folder = await ResolveAsync(folderPath, ct);

        if (folder.MimeType != DrivePolicy.FolderType)
            throw new InvalidOperationException($"Not a folder: {folderPath}");

        var entries = new List<DriveEntry>();
        string? pageToken = null;

        do
        {
            var page = await client.ListAsync(DrivePolicy.ChildrenQuery(folder.Id), pageToken, ListPageSize, ct);

            entries.AddRange(page.Files.Select(f => new DriveEntry(
                WorkspacePath.Combine(folderPath, f.Name),
                f.MimeType == DrivePolicy.FolderType,
                f.MimeType,
                f.Size,
                f.Modified)));

            pageToken = page.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return entries;
    }

    public async Task<string> ReadTextAsync(string path, CancellationToken ct)
    {
        var file = await ResolveFileAsync(path, ct);

        if (!DrivePolicy.IsReadable(file.MimeType))
            throw new NotSupportedException($"{path} is {file.MimeType}; only text files, Google Docs, Sheets and Slides can be read.");

        if (file.Size > settings.MaxReadBytes)
            throw new InvalidOperationException($"{path} is {file.Size:N0} bytes; the limit is {settings.MaxReadBytes:N0}.");

        var bytes = await client.DownloadAsync(file.Id!, DrivePolicy.ExportTypeFor(file.MimeType), ct);

        // Google-native files report no size up front, so check the exported text too.
        if (bytes.Length > settings.MaxReadBytes)
            throw new InvalidOperationException($"{path} exports to {bytes.Length:N0} bytes; the limit is {settings.MaxReadBytes:N0}.");

        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Replaces the content of an existing file. Creating files is deliberately not offered.</summary>
    public async Task WriteTextAsync(string path, string content, CancellationToken ct)
    {
        if (settings.ReadOnly)
            throw new InvalidOperationException("This server is configured as read-only.");

        var file = await ResolveFileAsync(path, ct);
        var uploadType = DrivePolicy.UploadTypeFor(file.MimeType, path);

        await client.UploadAsync(file.Id!, Encoding.UTF8.GetBytes(content), uploadType, ct);
    }

    private async Task<DriveFile> ResolveFileAsync(string path, CancellationToken ct)
    {
        var file = await ResolveAsync(WorkspacePath.Normalize(path), ct);

        return file.MimeType == DrivePolicy.FolderType
            ? throw new InvalidOperationException($"Path is a folder: {path}")
            : file;
    }

    /// <summary>Walks the path one name at a time from "/", so only shared items (or the root folder) are reachable.</summary>
    private async Task<DriveFile> ResolveAsync(string path, CancellationToken ct)
    {
        var current = new DriveFile(settings.RootFolderId, "/", DrivePolicy.FolderType);

        foreach (var name in WorkspacePath.Parts(path))
        {
            if (current.MimeType != DrivePolicy.FolderType)
                throw new FileNotFoundException($"Not found: {path}");

            var page = await client.ListAsync(DrivePolicy.ChildNamedQuery(current.Id, name), null, LookupPageSize, ct);
            current = DrivePolicy.SingleMatch(page.Files, name, path);
        }

        return current;
    }
}
