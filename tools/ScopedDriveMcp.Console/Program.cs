using Microsoft.Extensions.Configuration;
using ScopedDriveMcp;

// A direct way to try the server's Drive access without Claude or MCP in the loop.
// It uses the same config, DriveWorkspace and GoogleDriveClient as the MCP server.
//
//   scoped-drive list [path]
//   scoped-drive read <path>
//   scoped-drive write <path> <local-file>

var configPath = Environment.GetEnvironmentVariable("SCOPED_DRIVE_CONFIG")
    ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    var configuration = new ConfigurationBuilder().AddJsonFile(configPath, optional: false).Build();
    var settings = DriveSettings.Load(configuration);
    var workspace = new DriveWorkspace(new GoogleDriveClient(settings), settings);

    Console.Error.WriteLine($"Config: {configPath}");
    Console.Error.WriteLine($"Root:   {settings.RootFolderId ?? "(everything shared with the service account)"}");
    Console.Error.WriteLine($"Mode:   {(settings.ReadOnly ? "read-only" : "read/write")}");
    Console.Error.WriteLine();

    switch (args)
    {
        case [] or ["list"]:
            await List("/");
            break;

        case ["list", var path]:
            await List(path);
            break;

        case ["read", var path]:
            Console.WriteLine(await workspace.ReadTextAsync(path, cts.Token));
            break;

        case ["write", var path, var localFile]:
            await workspace.WriteTextAsync(path, await File.ReadAllTextAsync(localFile, cts.Token), cts.Token);
            Console.WriteLine($"Updated {path}");
            break;

        default:
            Console.Error.WriteLine("Usage: scoped-drive list [path] | read <path> | write <path> <local-file>");
            return 2;
    }

    return 0;

    async Task List(string path)
    {
        var entries = await workspace.ListAsync(path, cts.Token);

        if (entries.Count == 0)
            Console.WriteLine("(nothing here)");

        foreach (var entry in entries)
            Console.WriteLine($"{(entry.IsFolder ? "[Folder]" : "[File]"),-9}{entry.Path}");
    }
}
catch (OperationCanceledException)
{
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}
