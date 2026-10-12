using Google.Apis.Auth.OAuth2;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using File = Google.Apis.Drive.v3.Data.File;

namespace ScopedDriveMcp;

/// <summary>The real IDriveClient: signs in with the configured credential and calls the Google Drive API.</summary>
public sealed class GoogleDriveClient(DriveSettings settings) : IDriveClient
{
    private const string Fields = "id, name, mimeType, modifiedTime, size";

    private readonly SemaphoreSlim initLock = new(1, 1);
    private DriveService? drive;

    public async Task<DriveFilePage> ListAsync(string query, string? pageToken, int pageSize, CancellationToken ct)
    {
        var service = await GetDriveAsync(ct);

        var request = service.Files.List();
        request.Q = query;
        request.Fields = $"nextPageToken, files({Fields})";
        request.OrderBy = "folder,name";
        request.PageSize = pageSize;
        request.PageToken = pageToken;

        var result = await request.ExecuteAsync(ct);

        var files = (result.Files ?? [])
            .Select(f => new DriveFile(f.Id, f.Name, f.MimeType, f.Size, f.ModifiedTimeDateTimeOffset))
            .ToList();

        return new DriveFilePage(files, result.NextPageToken);
    }

    public async Task<byte[]> DownloadAsync(string fileId, string? exportMimeType, CancellationToken ct)
    {
        var service = await GetDriveAsync(ct);
        using var stream = new MemoryStream();

        var progress = exportMimeType is null
            ? await service.Files.Get(fileId).DownloadAsync(stream, ct)
            : await service.Files.Export(fileId, exportMimeType).DownloadAsync(stream, ct);

        if (progress.Status == DownloadStatus.Failed)
            throw progress.Exception ?? new InvalidOperationException($"Download failed for file {fileId}.");

        return stream.ToArray();
    }

    public async Task UploadAsync(string fileId, byte[] content, string mimeType, CancellationToken ct)
    {
        var service = await GetDriveAsync(ct);
        using var stream = new MemoryStream(content);

        var progress = await service.Files.Update(new File(), fileId, stream, mimeType).UploadAsync(ct);

        if (progress.Status == UploadStatus.Failed)
            throw progress.Exception ?? new InvalidOperationException($"Upload failed for file {fileId}.");
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
}
