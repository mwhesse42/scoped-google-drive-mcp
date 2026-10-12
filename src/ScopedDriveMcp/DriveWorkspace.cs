using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using File = Google.Apis.Drive.v3.Data.File;

namespace ScopedDriveMcp;

public sealed record DriveEntry(string Path, bool IsFolder, string MimeType, long? Size, DateTimeOffset? Modified);

/// <summary>
/// Google Drive access limited to what has been shared with the signed-in account.
/// "/" is either everything shared with it, or the configured RootFolderId. Paths look like "/Projects/notes".
/// </summary>
public sealed class DriveWorkspace(DriveSettings settings)
{
    private const string Fields = "id, name, mimeType, modifiedTime, size";

    private readonly SemaphoreSlim initLock = new(1, 1);
    private DriveService? drive;

    public async Task<IReadOnlyList<DriveEntry>> ListAsync(string path, CancellationToken ct)
    {
        var folderPath = WorkspacePath.Normalize(path);
        var folder = await ResolveAsync(folderPath, ct);

        if (folder.MimeType != DrivePolicy.FolderType)
            throw new InvalidOperationException($"Not a folder: {folderPath}");

        var service = await GetDriveAsync(ct);
        var entries = new List<DriveEntry>();
        string? pageToken = null;

        do
        {
            var request = service.Files.List();
            request.Q = DrivePolicy.ChildrenQuery(folder.Id);
            request.Fields = $"nextPageToken, files({Fields})";
            request.OrderBy = "folder,name";
            request.PageSize = 1000;
            request.PageToken = pageToken;

            var result = await request.ExecuteAsync(ct);
            entries.AddRange((result.Files ?? []).Select(f => ToEntry(f, WorkspacePath.Combine(folderPath, f.Name))));
            pageToken = result.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return entries;
    }

    public async Task<string> ReadTextAsync(string path, CancellationToken ct)
    {
        var file = await ResolveFileAsync(path, ct);

        if (!DrivePolicy.IsReadable(file.MimeType))
            throw new NotSupportedException($"{path} is {file.MimeType}; only text files, Google Docs, Sheets and Slides can be read.");

        if (file.Size > DrivePolicy.MaxReadBytes)
            throw new InvalidOperationException($"{path} is {file.Size:N0} bytes; the limit is {DrivePolicy.MaxReadBytes:N0}.");

        var service = await GetDriveAsync(ct);
        var exportType = DrivePolicy.ExportTypeFor(file.MimeType);
        using var stream = new MemoryStream();

        var progress = exportType is null
            ? await service.Files.Get(file.Id).DownloadAsync(stream, ct)
            : await service.Files.Export(file.Id, exportType).DownloadAsync(stream, ct);

        if (progress.Status == DownloadStatus.Failed)
            throw progress.Exception ?? new InvalidOperationException($"Download failed: {path}");

        // Google-native files report no size up front, so check the exported text too.
        if (stream.Length > DrivePolicy.MaxReadBytes)
            throw new InvalidOperationException($"{path} exports to {stream.Length:N0} bytes; the limit is {DrivePolicy.MaxReadBytes:N0}.");

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Replaces the content of an existing file. Creating files is deliberately not offered.</summary>
    public async Task WriteTextAsync(string path, string content, CancellationToken ct)
    {
        if (settings.ReadOnly)
            throw new InvalidOperationException("This server is configured as read-only.");

        var file = await ResolveFileAsync(path, ct);
        var uploadType = DrivePolicy.UploadTypeFor(file.MimeType, path);

        var service = await GetDriveAsync(ct);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var progress = await service.Files.Update(new File(), file.Id, stream, uploadType).UploadAsync(ct);

        if (progress.Status == UploadStatus.Failed)
            throw progress.Exception ?? new InvalidOperationException($"Upload failed: {path}");
    }

    private async Task<File> ResolveFileAsync(string path, CancellationToken ct)
    {
        var file = await ResolveAsync(WorkspacePath.Normalize(path), ct);

        return file.MimeType == DrivePolicy.FolderType
            ? throw new InvalidOperationException($"Path is a folder: {path}")
            : file;
    }

    /// <summary>Walks the path one name at a time from "/", so only shared items (or the root folder) are reachable.</summary>
    private async Task<File> ResolveAsync(string path, CancellationToken ct)
    {
        var service = await GetDriveAsync(ct);

        // Id = null is the virtual "/" holding everything shared with the account.
        var current = new File { Id = settings.RootFolderId, Name = "/", MimeType = DrivePolicy.FolderType };

        foreach (var name in WorkspacePath.Parts(path))
        {
            if (current.MimeType != DrivePolicy.FolderType)
                throw new FileNotFoundException($"Not found: {path}");

            var request = service.Files.List();
            request.Q = DrivePolicy.ChildNamedQuery(current.Id, name);
            request.Fields = $"files({Fields})";
            request.PageSize = 10;

            var result = await request.ExecuteAsync(ct);
            current = DrivePolicy.SingleMatch((result.Files ?? []).ToList(), name, path);
        }

        return current;
    }

    private async Task<DriveService> GetDriveAsync(CancellationToken ct)
    {
        if (drive is not null)
            return drive;

        await initLock.WaitAsync(ct);

        try
        {
            return drive ??= new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = await CreateCredentialAsync(ct),
                ApplicationName = "scoped-google-drive-mcp"
            });
        }
        finally
        {
            initLock.Release();
        }
    }

    private async Task<IConfigurableHttpClientInitializer> CreateCredentialAsync(CancellationToken ct)
    {
        // Ask Google only for what the configuration needs.
        var scope = settings.ReadOnly ? DriveService.Scope.DriveReadonly : DriveService.Scope.Drive;

        if (settings.ServiceAccountKeyFile is not null)
        {
#pragma warning disable CS0618 // FromFile is flagged for untrusted key files; this key is our own.
            return GoogleCredential.FromFile(settings.ServiceAccountKeyFile).CreateScoped(scope);
#pragma warning restore CS0618
        }

        if (settings.CredentialsFile is null || settings.TokenDirectory is null)
            throw new InvalidOperationException(
                "Set GoogleDrive:ServiceAccountKeyFile, or both GoogleDrive:CredentialsFile and GoogleDrive:TokenDirectory.");

        Directory.CreateDirectory(settings.TokenDirectory);

        return await GoogleWebAuthorizationBroker.AuthorizeAsync(
            GoogleClientSecrets.FromFile(settings.CredentialsFile).Secrets,
            [scope],
            "user",
            ct,
            new FileDataStore(settings.TokenDirectory, true));
    }

    private static DriveEntry ToEntry(File file, string path) =>
        new(path, file.MimeType == DrivePolicy.FolderType, file.MimeType, file.Size, file.ModifiedTimeDateTimeOffset);
}
