namespace CorsixNet.Formats.Tests;

public sealed class ThemeHospitalInstallTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("corsixnet-th-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void OpensFolderWithRequiredFiles()
    {
        CreateGameFiles(_root);

        Assert.True(ThemeHospitalInstall.TryOpen(_root, out var install, out var error), error);
        Assert.Equal(Path.GetFullPath(_root), install.RootPath);
        Assert.False(install.IsDemo);
    }

    [Fact]
    public void OpensEaLayoutFromInstallationFolder()
    {
        var game = Path.Combine(_root, "data", "Game");
        CreateGameFiles(game);

        Assert.True(ThemeHospitalInstall.TryOpen(_root, out var install, out var error), error);
        Assert.Equal(Path.GetFullPath(game), install.RootPath);
    }

    [Fact]
    public void ResolvesFileNamesIgnoringCase()
    {
        CreateGameFiles(_root, upperCase: false);

        Assert.True(ThemeHospitalInstall.TryOpen(_root, out var install, out var error), error);
        Assert.Equal([1, 2, 3], install.ReadAllBytes("Data", "VBlk-0.tab"));
        Assert.True(install.FileExists("data", "vblk-0.tab"));
    }

    [Fact]
    public void DetectsDemoFiles()
    {
        CreateGameFiles(_root);
        WriteFile(Path.Combine(_root, "DATAM", "DEMO.DAT"));

        Assert.True(ThemeHospitalInstall.TryOpen(_root, out var install, out var error), error);
        Assert.True(install.IsDemo);
    }

    [Fact]
    public void ReportsMissingFiles()
    {
        WriteFile(Path.Combine(_root, "DATA", "VBLK-0.TAB"));

        Assert.False(ThemeHospitalInstall.TryOpen(_root, out var install, out var error));
        Assert.Null(install);
        Assert.Contains("LEVELS/LEVEL.L1", error);
        Assert.Contains("QDATA/SPOINTER.DAT", error);
    }

    [Fact]
    public void ReportsMissingFolder()
    {
        Assert.False(ThemeHospitalInstall.TryOpen(Path.Combine(_root, "absent"), out _, out var error));
        Assert.Contains("does not exist", error);
    }

    [Fact]
    public void OpenReadThrowsForMissingFile()
    {
        CreateGameFiles(_root);
        Assert.True(ThemeHospitalInstall.TryOpen(_root, out var install, out _));

        Assert.Throws<FileNotFoundException>(() => install.OpenRead("DATA", "NOPE.DAT"));
        Assert.Null(install.FindFile("DATA", "NOPE.DAT"));
    }

    [Fact]
    public void RejectsWildcardsInNames()
    {
        CreateGameFiles(_root);
        Assert.True(ThemeHospitalInstall.TryOpen(_root, out var install, out _));

        Assert.Throws<ArgumentException>(() => install.FindFile("DATA", "*.TAB"));
    }

    [Fact]
    public void ListsTheMusicInTheOrderAndWithTheTitlesOfTheListFile()
    {
        CreateGameFiles(_root);
        var midi = Path.Combine(_root, "SOUND", "MIDI");
        foreach (var name in (string[])["ATLANTIS.XMI", "beachy.xmi", "Zombie.XMI", "NOTES.DOC"])
        {
            WriteFile(Path.Combine(midi, name));
        }

        WriteFile(Path.Combine(midi, "MIDI.TXT"), System.Text.Encoding.ASCII.GetBytes("BEACHY.XMI\r\nBeachy Head\r\n\r\natlantis.xmi\r\nAtlantis\r\nMISSING.XMI\r\nGone\r\n\x1A"));
        Assert.True(ThemeHospitalInstall.TryOpen(_root, out var install, out var error), error);

        var tracks = install.MusicTracks();

        Assert.Equal(["Beachy Head", "Atlantis", "Zombie"], tracks.Select(track => track.Title));
        Assert.Equal(["beachy.xmi", "ATLANTIS.XMI", "Zombie.XMI"], tracks.Select(track => Path.GetFileName(track.Path)));
    }

    [Fact]
    public void WithoutAListFileTheMusicIsTitledAndOrderedByName()
    {
        CreateGameFiles(_root);
        WriteFile(Path.Combine(_root, "SOUND", "MIDI", "B.XMI"));
        WriteFile(Path.Combine(_root, "SOUND", "MIDI", "a.xmi"));
        Assert.True(ThemeHospitalInstall.TryOpen(_root, out var install, out var error), error);

        Assert.Equal(["a", "B"], install.MusicTracks().Select(track => track.Title));
        Directory.Delete(Path.Combine(_root, "SOUND"), recursive: true);
        Assert.Empty(install.MusicTracks());
    }

    private static void CreateGameFiles(string root, bool upperCase = true)
    {
        string Name(string name) => upperCase ? name : name.ToLowerInvariant();

        WriteFile(Path.Combine(root, Name("DATA"), Name("VBLK-0.TAB")), [1, 2, 3]);
        WriteFile(Path.Combine(root, Name("LEVELS"), Name("LEVEL.L1")));
        WriteFile(Path.Combine(root, Name("QDATA"), Name("SPOINTER.DAT")));
    }

    private static void WriteFile(string path, byte[]? contents = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, contents ?? []);
    }
}
