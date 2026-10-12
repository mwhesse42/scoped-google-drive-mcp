namespace ScopedDriveMcp;

public static class WorkspacePath
{
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Any(p => p is "." or ".."))
            throw new InvalidOperationException("Path traversal is not allowed.");

        return "/" + string.Join('/', parts);
    }

    public static IReadOnlyList<string> Parts(string path) =>
        Normalize(path).Split('/', StringSplitOptions.RemoveEmptyEntries);

    public static string Combine(string folder, string name) =>
        folder == "/" ? "/" + name : $"{folder}/{name}";
}
