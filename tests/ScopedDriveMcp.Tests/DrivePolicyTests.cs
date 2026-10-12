namespace ScopedDriveMcp.Tests;

public class DrivePolicyTests
{
    [Fact]
    public void Root_without_a_folder_id_lists_everything_shared_with_the_account() =>
        Assert.Equal("sharedWithMe = true and trashed = false", DrivePolicy.ChildrenQuery(null));

    [Fact]
    public void Folder_children_are_queried_by_parent_id() =>
        Assert.Equal("'abc123' in parents and trashed = false", DrivePolicy.ChildrenQuery("abc123"));

    [Fact]
    public void Name_lookup_escapes_quotes_so_a_file_name_cannot_change_the_query() =>
        Assert.Equal(
            @"'abc123' in parents and name = 'O\'Brien\' or name != \'x' and trashed = false",
            DrivePolicy.ChildNamedQuery("abc123", "O'Brien' or name != 'x"));

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("it's", @"it\'s")]
    [InlineData(@"back\slash", @"back\\slash")]
    public void EscapeQuery_escapes_quotes_and_backslashes(string input, string expected) =>
        Assert.Equal(expected, DrivePolicy.EscapeQuery(input));

    [Theory]
    [InlineData(DrivePolicy.DocumentType, "text/plain")]
    [InlineData(DrivePolicy.SpreadsheetType, "text/csv")]
    [InlineData(DrivePolicy.PresentationType, "text/plain")]
    [InlineData("text/plain", null)]
    [InlineData("application/pdf", null)]
    public void ExportTypeFor_maps_google_files_to_text(string mimeType, string? expected) =>
        Assert.Equal(expected, DrivePolicy.ExportTypeFor(mimeType));

    [Theory]
    [InlineData("text/plain", true)]
    [InlineData("text/markdown", true)]
    [InlineData("application/json", true)]
    [InlineData(DrivePolicy.DocumentType, true)]
    [InlineData(DrivePolicy.SpreadsheetType, true)]
    [InlineData("application/pdf", false)]
    [InlineData("image/png", false)]
    [InlineData(DrivePolicy.FolderType, false)]
    public void IsReadable_allows_text_and_google_files_only(string mimeType, bool expected) =>
        Assert.Equal(expected, DrivePolicy.IsReadable(mimeType));

    [Theory]
    [InlineData(DrivePolicy.DocumentType, "text/plain")]
    [InlineData("text/markdown", "text/markdown")]
    public void UploadTypeFor_allows_docs_and_text_files(string mimeType, string expected) =>
        Assert.Equal(expected, DrivePolicy.UploadTypeFor(mimeType, "/file"));

    [Theory]
    [InlineData(DrivePolicy.SpreadsheetType)]
    [InlineData(DrivePolicy.PresentationType)]
    [InlineData("application/pdf")]
    [InlineData("image/png")]
    public void UploadTypeFor_rejects_files_that_cannot_be_written_as_text(string mimeType) =>
        Assert.Throws<NotSupportedException>(() => DrivePolicy.UploadTypeFor(mimeType, "/file"));

    [Fact]
    public void SingleMatch_returns_the_only_match() =>
        Assert.Equal("a", DrivePolicy.SingleMatch(new[] { "a" }, "notes", "/notes"));

    [Fact]
    public void SingleMatch_reports_missing_files() =>
        Assert.Throws<FileNotFoundException>(() => DrivePolicy.SingleMatch(Array.Empty<string>(), "notes", "/notes"));

    [Fact]
    public void SingleMatch_refuses_to_guess_between_duplicate_names()
    {
        var error = Assert.Throws<InvalidOperationException>(() => DrivePolicy.SingleMatch(new[] { "a", "b" }, "notes", "/notes"));
        Assert.Contains("2 items named 'notes'", error.Message);
    }
}
