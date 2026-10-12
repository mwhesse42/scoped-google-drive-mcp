using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AI.Workspace.Mcp;

[McpServerToolType]
public static class DriveTools
{
    [McpServerTool(Name = "list_folder", ReadOnly = true)]
    [Description("List the files and folders at a path inside the shared Google Drive folder. \"/\" is the shared folder itself.")]
    public static Task<string> ListFolder(
        DriveWorkspace workspace,
        [Description("Folder path, e.g. \"/\" or \"/Projects/2026\".")] string path = "/",
        CancellationToken ct = default) =>
        Run(async () =>
        {
            var entries = await workspace.ListAsync(path, ct);

            return entries.Count == 0
                ? "(empty folder)"
                : string.Join('\n', entries.Select(e => $"{(e.IsFolder ? "[Folder]" : "[File]"),-9}{e.Path}"));
        });

    [McpServerTool(Name = "read_file", ReadOnly = true)]
    [Description("Read a file as text. Google Docs and Slides come back as plain text, Google Sheets as CSV.")]
    public static Task<string> ReadFile(
        DriveWorkspace workspace,
        [Description("File path, e.g. \"/Projects/notes.md\".")] string path,
        CancellationToken ct = default) =>
        Run(() => workspace.ReadTextAsync(path, ct));

    [McpServerTool(Name = "write_file")]
    [Description("Replace the entire content of an existing file (text files or Google Docs). It cannot create new files.")]
    public static Task<string> WriteFile(
        DriveWorkspace workspace,
        [Description("Path of an existing file.")] string path,
        [Description("The new full content of the file.")] string content,
        CancellationToken ct = default) =>
        Run(async () =>
        {
            await workspace.WriteTextAsync(path, content, ct);
            return $"Updated {path}";
        });

    // Turn failures into messages the model can read instead of a generic tool error.
    private static async Task<string> Run(Func<Task<string>> action)
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
