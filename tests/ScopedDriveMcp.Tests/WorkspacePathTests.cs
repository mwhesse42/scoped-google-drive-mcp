namespace ScopedDriveMcp.Tests;

public class WorkspacePathTests
{
    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    [InlineData("notes.md", "/notes.md")]
    [InlineData("/Projects/2026/", "/Projects/2026")]
    [InlineData("//Projects//2026", "/Projects/2026")]
    [InlineData(@"\Projects\2026", "/Projects/2026")]
    [InlineData(" /Projects / 2026 ", "/Projects/2026")]
    public void Normalize_produces_a_clean_absolute_path(string? input, string expected) =>
        Assert.Equal(expected, WorkspacePath.Normalize(input));

    [Theory]
    [InlineData("..")]
    [InlineData("/Projects/../Taxes")]
    [InlineData("./notes.md")]
    [InlineData(@"Projects\..\Taxes")]
    public void Normalize_rejects_path_traversal(string input) =>
        Assert.Throws<InvalidOperationException>(() => WorkspacePath.Normalize(input));

    [Fact]
    public void Parts_of_root_is_empty() =>
        Assert.Empty(WorkspacePath.Parts("/"));

    [Fact]
    public void Parts_splits_each_folder_name() =>
        Assert.Equal(new[] { "Projects", "2026", "notes.md" }, WorkspacePath.Parts("/Projects/2026/notes.md"));

    [Theory]
    [InlineData("/", "notes.md", "/notes.md")]
    [InlineData("/Projects", "notes.md", "/Projects/notes.md")]
    public void Combine_joins_folder_and_name(string folder, string name, string expected) =>
        Assert.Equal(expected, WorkspacePath.Combine(folder, name));
}
