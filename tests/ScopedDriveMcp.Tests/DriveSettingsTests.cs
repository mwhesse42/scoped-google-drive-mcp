using Microsoft.Extensions.Configuration;

namespace ScopedDriveMcp.Tests;

public class DriveSettingsTests
{
    private static DriveSettings Load(params (string Key, string? Value)[] values) =>
        DriveSettings.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>($"GoogleDrive:{v.Key}", v.Value)))
            .Build());

    [Fact]
    public void Empty_root_folder_means_everything_shared()
    {
        var settings = Load(("ServiceAccountKeyFile", "key.json"), ("RootFolderId", ""));

        Assert.Null(settings.RootFolderId);
        Assert.Equal("key.json", settings.ServiceAccountKeyFile);
    }

    [Fact]
    public void Root_folder_id_is_kept_when_set() =>
        Assert.Equal("abc123", Load(("RootFolderId", " abc123 ")).RootFolderId);

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    [InlineData("yes", false)]
    public void ReadOnly_is_off_unless_explicitly_true(string? value, bool expected) =>
        Assert.Equal(expected, Load(("ReadOnly", value)).ReadOnly);
}
