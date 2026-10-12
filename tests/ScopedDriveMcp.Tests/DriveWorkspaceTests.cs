using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ScopedDriveMcp.Tests;

public class DriveWorkspaceTests
{
    private const string Doc = DrivePolicy.DocumentType;
    private const string Sheet = DrivePolicy.SpreadsheetType;
    private const string Folder = DrivePolicy.FolderType;

    private readonly Mock<IDriveClient> client = new(MockBehavior.Strict);
    private readonly CancellationToken ct = CancellationToken.None;

    private DriveWorkspace Workspace(string? rootFolderId = null, bool readOnly = false) =>
        new(client.Object, new DriveSettings { RootFolderId = rootFolderId, ReadOnly = readOnly });

    // Answers a name lookup under a parent folder (null = the shared root).
    private void GivenChild(string? parentId, string name, params DriveFile[] matches) =>
        client.Setup(c => c.ListAsync(DrivePolicy.ChildNamedQuery(parentId, name), null, DriveWorkspace.LookupPageSize, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new DriveFilePage(matches));

    [Fact]
    public async Task Root_lists_everything_shared_with_the_account()
    {
        client.Setup(c => c.ListAsync("sharedWithMe = true and trashed = false", null, DriveWorkspace.ListPageSize, ct))
              .ReturnsAsync(new DriveFilePage([new("f1", "Projects", Folder), new("d1", "notes", Doc)]));

        var entries = await Workspace().ListAsync("/", ct);

        Assert.Equal(new[] { "/Projects", "/notes" }, entries.Select(e => e.Path));
        Assert.True(entries[0].IsFolder);
        Assert.False(entries[1].IsFolder);
    }

    [Fact]
    public async Task Root_folder_id_replaces_the_shared_root()
    {
        client.Setup(c => c.ListAsync("'root123' in parents and trashed = false", null, DriveWorkspace.ListPageSize, ct))
              .ReturnsAsync(new DriveFilePage([new("d1", "notes", Doc)]));

        var entries = await Workspace(rootFolderId: "root123").ListAsync("/", ct);

        Assert.Equal("/notes", Assert.Single(entries).Path);
    }

    [Fact]
    public async Task Listing_follows_every_page()
    {
        var query = DrivePolicy.ChildrenQuery(null);
        client.Setup(c => c.ListAsync(query, null, DriveWorkspace.ListPageSize, ct))
              .ReturnsAsync(new DriveFilePage([new("a", "a.txt", "text/plain")], NextPageToken: "page2"));
        client.Setup(c => c.ListAsync(query, "page2", DriveWorkspace.ListPageSize, ct))
              .ReturnsAsync(new DriveFilePage([new("b", "b.txt", "text/plain")]));

        var entries = await Workspace().ListAsync("/", ct);

        Assert.Equal(new[] { "/a.txt", "/b.txt" }, entries.Select(e => e.Path));
    }

    [Fact]
    public async Task Listing_a_file_is_an_error()
    {
        GivenChild(null, "notes.txt", new DriveFile("t1", "notes.txt", "text/plain"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Workspace().ListAsync("/notes.txt", ct));
    }

    [Fact]
    public async Task Paths_are_resolved_one_folder_at_a_time()
    {
        GivenChild(null, "Projects", new DriveFile("f1", "Projects", Folder));
        GivenChild("f1", "notes.md", new DriveFile("t1", "notes.md", "text/markdown", Size: 5));
        client.Setup(c => c.DownloadAsync("t1", null, ct)).ReturnsAsync(Encoding.UTF8.GetBytes("hello"));

        var text = await Workspace().ReadTextAsync("/Projects/notes.md", ct);

        Assert.Equal("hello", text);
        client.VerifyAll();
    }

    [Fact]
    public async Task Google_docs_are_exported_as_plain_text()
    {
        GivenChild(null, "plan", new DriveFile("d1", "plan", Doc));
        client.Setup(c => c.DownloadAsync("d1", "text/plain", ct)).ReturnsAsync(Encoding.UTF8.GetBytes("the plan"));

        Assert.Equal("the plan", await Workspace().ReadTextAsync("/plan", ct));
    }

    [Fact]
    public async Task Missing_paths_are_reported()
    {
        GivenChild(null, "Taxes");

        await Assert.ThrowsAsync<FileNotFoundException>(() => Workspace().ReadTextAsync("/Taxes/2025.txt", ct));
    }

    [Fact]
    public async Task Duplicate_names_are_refused_rather_than_guessed()
    {
        GivenChild(null, "notes.txt",
            new DriveFile("t1", "notes.txt", "text/plain"),
            new DriveFile("t2", "notes.txt", "text/plain"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Workspace().ReadTextAsync("/notes.txt", ct));

        Assert.Contains("2 items named 'notes.txt'", error.Message);
        client.Verify(c => c.DownloadAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Binary_files_are_not_downloaded()
    {
        GivenChild(null, "scan.pdf", new DriveFile("p1", "scan.pdf", "application/pdf", Size: 100));

        await Assert.ThrowsAsync<NotSupportedException>(() => Workspace().ReadTextAsync("/scan.pdf", ct));

        client.Verify(c => c.DownloadAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Large_files_are_refused_before_download()
    {
        GivenChild(null, "big.log", new DriveFile("l1", "big.log", "text/plain", Size: DriveSettings.DefaultMaxReadBytes + 1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Workspace().ReadTextAsync("/big.log", ct));

        client.Verify(c => c.DownloadAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Large_exports_are_refused()
    {
        GivenChild(null, "huge", new DriveFile("d1", "huge", Doc));
        client.Setup(c => c.DownloadAsync("d1", "text/plain", ct)).ReturnsAsync(new byte[DriveSettings.DefaultMaxReadBytes + 1]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Workspace().ReadTextAsync("/huge", ct));
    }

    [Fact]
    public async Task Writing_a_doc_uploads_the_new_text()
    {
        GivenChild(null, "plan", new DriveFile("d1", "plan", Doc));
        client.Setup(c => c.UploadAsync("d1", It.IsAny<byte[]>(), "text/plain", ct)).Returns(Task.CompletedTask);

        await Workspace().WriteTextAsync("/plan", "new plan", ct);

        client.Verify(c => c.UploadAsync("d1",
            It.Is<byte[]>(b => Encoding.UTF8.GetString(b) == "new plan"), "text/plain", ct), Times.Once);
    }

    [Fact]
    public async Task Writing_a_sheet_is_refused()
    {
        GivenChild(null, "budget", new DriveFile("s1", "budget", Sheet));

        await Assert.ThrowsAsync<NotSupportedException>(() => Workspace().WriteTextAsync("/budget", "x", ct));

        client.Verify(c => c.UploadAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Read_only_mode_never_touches_drive_on_write()
    {
        // Strict mock with no setups: any call to Drive would throw.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Workspace(readOnly: true).WriteTextAsync("/plan", "x", ct));

        client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Workspace_resolves_from_dependency_injection_with_a_mocked_client()
    {
        client.Setup(c => c.ListAsync(DrivePolicy.ChildrenQuery(null), null, DriveWorkspace.ListPageSize, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new DriveFilePage([]));

        using var provider = new ServiceCollection()
            .AddSingleton(new DriveSettings())
            .AddSingleton(client.Object)
            .AddSingleton<DriveWorkspace>()
            .BuildServiceProvider();

        var entries = await provider.GetRequiredService<DriveWorkspace>().ListAsync("/", ct);

        Assert.Empty(entries);
    }
}
