using OpenWards.Content.Settings;

namespace OpenWards.Content.Tests;

/// <summary>Settings and saves from before the game was renamed from CorsixNet to OpenWards.</summary>
public sealed class LegacyFoldersTests : IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), "OpenWardsLegacyFoldersTests", Guid.NewGuid().ToString("N"));

    public LegacyFoldersTests() => Directory.CreateDirectory(_parent);

    public void Dispose()
    {
        if (Directory.Exists(_parent))
        {
            Directory.Delete(_parent, recursive: true);
        }
    }

    private string Legacy => Path.Combine(_parent, LegacyFolders.LegacyFolderName);

    private string Current => Path.Combine(_parent, LegacyFolders.FolderName);

    [Fact]
    public void MovesTheOldFolderWithItsSettingsAndSaves()
    {
        Directory.CreateDirectory(Path.Combine(Legacy, "Saves", "Autosaves"));
        File.WriteAllText(Path.Combine(Legacy, "config.yaml"), "language: 'Russian'\n");
        File.WriteAllText(Path.Combine(Legacy, "Saves", "quicksave.qs"), "save");
        File.WriteAllText(Path.Combine(Legacy, "Saves", "Autosaves", "Autosave-01-31.sav"), "auto");

        Assert.NotNull(LegacyFolders.Migrate(_parent));

        Assert.False(Directory.Exists(Legacy));
        Assert.Equal("language: 'Russian'\n", File.ReadAllText(Path.Combine(Current, "config.yaml")));
        Assert.Equal("save", File.ReadAllText(Path.Combine(Current, "Saves", "quicksave.qs")));
        Assert.Equal("auto", File.ReadAllText(Path.Combine(Current, "Saves", "Autosaves", "Autosave-01-31.sav")));
    }

    [Fact]
    public void PointsSettingsThatLedIntoTheOldFolderAtTheNewOne()
    {
        Directory.CreateDirectory(Legacy);
        var saves = Path.Combine(Legacy, "MySaves");
        File.WriteAllText(Path.Combine(Legacy, "config.yaml"), $"savegames: '{saves}'\nlevels: '{Legacy}'\nscreenshots: '{Legacy}Shots'\n");

        LegacyFolders.Migrate(_parent);

        Assert.Equal(
            $"savegames: '{Path.Combine(Current, "MySaves")}'\nlevels: '{Current}'\nscreenshots: '{Legacy}Shots'\n",
            File.ReadAllText(Path.Combine(Current, "config.yaml")));
    }

    [Fact]
    public void LeavesBothAloneOnceTheNewFolderExists()
    {
        Directory.CreateDirectory(Legacy);
        Directory.CreateDirectory(Current);
        File.WriteAllText(Path.Combine(Legacy, "config.yaml"), "old");

        Assert.Null(LegacyFolders.Migrate(_parent));

        Assert.True(File.Exists(Path.Combine(Legacy, "config.yaml")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Current));
    }

    [Fact]
    public void DoesNothingWithoutAnOldFolder()
    {
        Assert.Null(LegacyFolders.Migrate(_parent));
        Assert.False(Directory.Exists(Current));
    }
}
