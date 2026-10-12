using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ScopedDriveMcp;

[McpServerToolType]
public sealed class DriveReadTools
{
    [McpServerTool(Name = "list_folder", ReadOnly = true)]
    [Description("List files and folders shared with this server's Google account. \"/\" lists everything shared; pass a folder path to look inside it.")]
    public static Task<string> ListFolder(
        DriveWorkspace workspace,
        [Description("Folder path, e.g. \"/\" or \"/Projects/2026\".")] string path = "/",
        CancellationToken ct = default) =>
        ToolErrors.Run(async () =>
        {
            var entries = await workspace.ListAsync(path, ct);

            return entries.Count == 0
                ? "(nothing here)"
                : string.Join('\n', entries.Select(e => $"{(e.IsFolder ? "[Folder]" : "[File]"),-9}{e.Path}"));
        });

    [McpServerTool(Name = "read_file", ReadOnly = true)]
    [Description("Read a file as text. Google Docs and Slides come back as plain text, Google Sheets as CSV. Binary files such as PDFs and images are not supported.")]
    public static Task<string> ReadFile(
        DriveWorkspace workspace,
        [Description("File path, e.g. \"/Projects/notes.md\".")] string path,
        CancellationToken ct = default) =>
        ToolErrors.Run(() => workspace.ReadTextAsync(path, ct));
}

[McpServerToolType]
public sealed class DriveWriteTools
{
    [McpServerTool(Name = "write_file", Destructive = true)]
    [Description("Replace the entire content of an existing text file or Google Doc. It cannot create new files.")]
    public static Task<string> WriteFile(
        DriveWorkspace workspace,
        [Description("Path of an existing file.")] string path,
        [Description("The new full content of the file.")] string content,
        CancellationToken ct = default) =>
        ToolErrors.Run(async () =>
        {
            await workspace.WriteTextAsync(path, content, ct);
            return $"Updated {path}";
        });
}

internal static class ToolErrors
{
    // Turn failures into messages the model can read instead of a generic tool error.
    public static async Task<string> Run(Func<Task<string>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not McpException)
        {
            throw new McpException(ex.Message);
        }
    }
}
