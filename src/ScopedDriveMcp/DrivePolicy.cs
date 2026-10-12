namespace ScopedDriveMcp;

/// <summary>The rules for building Drive queries and handling file types, kept free of I/O so they can be unit tested.</summary>
internal static class DrivePolicy
{
    public const string FolderType = "application/vnd.google-apps.folder";
    public const string DocumentType = "application/vnd.google-apps.document";
    public const string SpreadsheetType = "application/vnd.google-apps.spreadsheet";
    public const string PresentationType = "application/vnd.google-apps.presentation";

    private const string GoogleNativePrefix = "application/vnd.google-apps.";

    /// <summary>A null folder ID is the virtual "/" that holds everything shared with the account.</summary>
    public static string ChildrenQuery(string? folderId) =>
        $"{ChildrenOf(folderId)} and trashed = false";

    public static string ChildNamedQuery(string? folderId, string name) =>
        $"{ChildrenOf(folderId)} and name = '{EscapeQuery(name)}' and trashed = false";

    public static string EscapeQuery(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");

    /// <summary>Google-native files have no bytes of their own; this is the text format to export them as.</summary>
    public static string? ExportTypeFor(string mimeType) => mimeType switch
    {
        DocumentType => "text/plain",
        SpreadsheetType => "text/csv",
        PresentationType => "text/plain",
        _ => null
    };

    public static bool IsReadable(string mimeType) =>
        ExportTypeFor(mimeType) is not null
        || mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || mimeType is "application/json" or "application/xml";

    /// <summary>The content type to upload when replacing a file, or an error if the file can't be written as text.</summary>
    public static string UploadTypeFor(string mimeType, string path)
    {
        if (mimeType == DocumentType)
            return "text/plain"; // Drive converts the text into the Doc's new body

        if (mimeType.StartsWith(GoogleNativePrefix, StringComparison.Ordinal))
            throw new NotSupportedException($"Writing this kind of Google file ({mimeType}) is not supported: {path}");

        if (!IsReadable(mimeType))
            throw new NotSupportedException($"Only text files and Google Docs can be written; {path} is {mimeType}.");

        return mimeType;
    }

    /// <summary>Drive allows duplicate names in a folder; refuse to guess which one was meant.</summary>
    public static T SingleMatch<T>(IReadOnlyList<T> matches, string name, string path) => matches.Count switch
    {
        0 => throw new FileNotFoundException($"Not found: {path}"),
        1 => matches[0],
        _ => throw new InvalidOperationException(
            $"There are {matches.Count} items named '{name}' in the same folder ({path}). Rename one of them in Google Drive.")
    };

    private static string ChildrenOf(string? folderId) =>
        folderId is null ? "sharedWithMe = true" : $"'{EscapeQuery(folderId)}' in parents";
}
