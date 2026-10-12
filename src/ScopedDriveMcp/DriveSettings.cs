using Microsoft.Extensions.Configuration;

namespace ScopedDriveMcp;

public sealed class DriveSettings
{
    /// <summary>Service account JSON key. Preferred: it can only see what is shared with it.</summary>
    public string? ServiceAccountKeyFile { get; init; }

    /// <summary>OAuth client secrets file. Used only when no service account key is set.</summary>
    public string? CredentialsFile { get; init; }

    public string? TokenDirectory { get; init; }

    /// <summary>
    /// Optional extra lock. When empty, "/" lists everything shared with the service account.
    /// When set, the tools can only reach this one folder, even if more is shared.
    /// </summary>
    public string? RootFolderId { get; init; }

    /// <summary>Request read-only Drive access and don't offer the write tool.</summary>
    public bool ReadOnly { get; init; }

    public const long DefaultMaxReadBytes = 1_000_000;

    /// <summary>Largest file read_file will return, so one call can't flood the model's context.</summary>
    public long MaxReadBytes { get; init; } = DefaultMaxReadBytes;

    public static DriveSettings Load(IConfiguration configuration)
    {
        var section = configuration.GetSection("GoogleDrive");

        return new DriveSettings
        {
            ServiceAccountKeyFile = Blank(section["ServiceAccountKeyFile"]),
            CredentialsFile = Blank(section["CredentialsFile"]),
            TokenDirectory = Blank(section["TokenDirectory"]),
            RootFolderId = Blank(section["RootFolderId"]),
            ReadOnly = bool.TryParse(section["ReadOnly"], out var readOnly) && readOnly,
            MaxReadBytes = long.TryParse(section["MaxReadBytes"], out var maxReadBytes) && maxReadBytes > 0
                ? maxReadBytes
                : DefaultMaxReadBytes
        };
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
