using Microsoft.Extensions.Configuration;

namespace AI.Workspace.Mcp;

public sealed class DriveSettings
{
    /// <summary>Service account JSON key. Preferred: it can only see folders shared with it.</summary>
    public string? ServiceAccountKeyFile { get; init; }

    /// <summary>OAuth client secrets file. Used only when no service account key is set.</summary>
    public string? CredentialsFile { get; init; }

    public string? TokenDirectory { get; init; }

    /// <summary>The only folder the tools can reach. Every path is resolved under it.</summary>
    public required string RootFolderId { get; init; }

    public static DriveSettings Load(IConfiguration configuration)
    {
        var section = configuration.GetSection("GoogleDrive");

        // Accept a direct RootFolderId, or the older EnvironmentName + Workspace:GoogleDriveRoots layout.
        var rootFolderId = section["RootFolderId"];

        if (string.IsNullOrWhiteSpace(rootFolderId) && configuration["EnvironmentName"] is { } environment)
            rootFolderId = configuration[$"Workspace:GoogleDriveRoots:{environment}"];

        if (string.IsNullOrWhiteSpace(rootFolderId))
            throw new InvalidOperationException("Set GoogleDrive:RootFolderId in the config file.");

        return new DriveSettings
        {
            ServiceAccountKeyFile = section["ServiceAccountKeyFile"],
            CredentialsFile = section["CredentialsFile"],
            TokenDirectory = section["TokenDirectory"],
            RootFolderId = rootFolderId
        };
    }
}
