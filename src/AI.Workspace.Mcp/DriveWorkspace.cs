using System.Text;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using File = Google.Apis.Drive.v3.Data.File;

namespace AI.Workspace.Mcp;

public sealed record DriveEntry(string Path, bool IsFolder, string MimeType, long? Size, DateTimeOffset? Modified);

/// <summary>Google Drive access sandboxed to one root folder. Paths look like "/Projects/notes".</summary>
public sealed class DriveWorkspace(DriveSettings settings)
{
    private const string FolderType = "application/vnd.google-apps.folder";
    private const string DocumentType = "application/vnd.google-apps.document";
    private const string SpreadsheetType = "application/vnd.google-apps.spreadsheet";
    private const string PresentationType = "application/vnd.google-apps.presentation";
    private const string Fields = "id, name, mimeType, modifiedTime, size";

    private readonly SemaphoreSlim initLock = new(1, 1);
    private DriveService? drive;

    public async Task<IReadOnlyList<DriveEntry>> ListAsync(string path, CancellationToken ct)
    {
        var folderPath = WorkspacePath.Normalize(path);
        var folder = await ResolveAsync(folderPath, ct);

        if (folder.MimeType != FolderType)
            throw new InvalidOperationException($"Not a folder: {folderPath}");

        var service = await GetDriveAsync(ct);
        var entries = new List<DriveEntry>();
        string? pageToken = null;

        do
        {
            var request = service.Files.List();
            request.Q = $"'{folder.Id}' in parents and trashed = false";
            request.Fields = $"nextPageToken, files({Fields})";
            request.OrderBy = "folder,name";
            request.PageSize = 1000;
            request.PageToken = pageToken;

            var result = await request.ExecuteAsync(ct);
            entries.AddRange(result.Files.Select(f => ToEntry(f, WorkspacePath.Combine(folderPath, f.Name))));
            pageToken = result.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return entries;
    }

    public async Task<string> ReadTextAsync(string path, CancellationToken ct)
    {
        var file = await ResolveFileAsync(path, ct);
        var service = await GetDriveAsync(ct);

        // Google-native files have no bytes of their own; export them to text.
        var exportType = file.MimeType switch
        {
            DocumentType => "text/plain",
            SpreadsheetType => "text/csv",
            PresentationType => "text/plain",
            _ => null
        };

        using var stream = new MemoryStream();

        var progress = exportType is null
            ? await service.Files.Get(file.Id).DownloadAsync(stream, ct)
            : await service.Files.Export(file.Id, exportType).DownloadAsync(stream, ct);

        if (progress.Status == DownloadStatus.Failed)
            throw progress.Exception ?? new InvalidOperationException($"Download failed: {path}");

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Replaces the content of an existing file. Creating files is deliberately not offered.</summary>
    public async Task WriteTextAsync(string path, string content, CancellationToken ct)
    {
        var file = await ResolveFileAsync(path, ct);

        var uploadType = file.MimeType switch
        {
            DocumentType => "text/plain", // Drive converts the text into the Doc's new body
            _ when file.MimeType.StartsWith("application/vnd.google-apps.") =>
                throw new NotSupportedException($"Writing {file.MimeType} files is not supported: {path}"),
            _ => file.MimeType
        };

        var service = await GetDriveAsync(ct);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var progress = await service.Files.Update(new File(), file.Id, stream, uploadType).UploadAsync(ct);

        if (progress.Status == UploadStatus.Failed)
            throw progress.Exception ?? new InvalidOperationException($"Upload failed: {path}");
    }

    private async Task<File> ResolveFileAsync(string path, CancellationToken ct)
    {
        var file = await ResolveAsync(WorkspacePath.Normalize(path), ct);

        return file.MimeType == FolderType
            ? throw new InvalidOperationException($"Path is a folder: {path}")
            : file;
    }

    /// <summary>Walks the path one name at a time from the root, so nothing outside the root is reachable.</summary>
    private async Task<File> ResolveAsync(string path, CancellationToken ct)
    {
        var service = await GetDriveAsync(ct);
        var current = new File { Id = settings.RootFolderId, Name = "/", MimeType = FolderType };

        foreach (var name in WorkspacePath.Parts(path))
        {
            if (current.MimeType != FolderType)
                throw new FileNotFoundException($"Not found: {path}");

            var request = service.Files.List();
            request.Q = $"'{current.Id}' in parents and name = '{EscapeQuery(name)}' and trashed = false";
            request.Fields = $"files({Fields})";
            request.PageSize = 1;

            var result = await request.ExecuteAsync(ct);

            current = result.Files.FirstOrDefault()
                ?? throw new FileNotFoundException($"Not found: {path}");
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
                ApplicationName = "AI Workspace MCP"
            });
        }
        finally
        {
            initLock.Release();
        }
    }

    private async Task<IConfigurableHttpClientInitializer> CreateCredentialAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(settings.ServiceAccountKeyFile))
        {
#pragma warning disable CS0618 // FromFile is flagged for untrusted key files; this key is our own.
            return GoogleCredential.FromFile(settings.ServiceAccountKeyFile).CreateScoped(DriveService.Scope.Drive);
#pragma warning restore CS0618
        }

        if (string.IsNullOrWhiteSpace(settings.CredentialsFile) || string.IsNullOrWhiteSpace(settings.TokenDirectory))
            throw new InvalidOperationException(
                "Set GoogleDrive:ServiceAccountKeyFile, or both GoogleDrive:CredentialsFile and GoogleDrive:TokenDirectory.");

        Directory.CreateDirectory(settings.TokenDirectory);

        return await GoogleWebAuthorizationBroker.AuthorizeAsync(
            GoogleClientSecrets.FromFile(settings.CredentialsFile).Secrets,
            [DriveService.Scope.Drive],
            "user",
            ct,
            new FileDataStore(settings.TokenDirectory, true));
    }

    private static DriveEntry ToEntry(File file, string path) =>
        new(path, file.MimeType == FolderType, file.MimeType, file.Size, file.ModifiedTimeDateTimeOffset);

    private static string EscapeQuery(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");
}
