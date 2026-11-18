using Microsoft.Xna.Framework;
using OpenWards.Client.Ui;
using OpenWards.Client.Ui.Windows;
using OpenWards.Content.Settings;

namespace OpenWards.Client.Tests;

public sealed class DirectoryBrowserTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "OpenWardsDirectoryBrowserTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>
    /// A made-up file system: C:\ with music (holding Rock), Games and Apps, and D:\ with nothing. The folders below the
    /// roots use forward slashes, which every system splits paths at.
    /// </summary>
    private static DirectoryTree FakeTree(List<string>? listed = null) => new(["C:\\", "D:\\"], path =>
    {
        listed?.Add(path);
        return path switch
        {
            "C:\\" => ["C:/music", "C:/Games", "C:/Apps"],
            "C:/music" => ["C:/music/Rock"],
            _ => [],
        };
    });

    [Fact]
    public void FoldersAreListedWhenOpenedAndSortedByNameInLowerCase()
    {
        var listed = new List<string>();
        var tree = FakeTree(listed);

        // Roots are taken to have children until opened; nothing is listed yet.
        Assert.Equal(["C:\\", "D:\\"], tree.Visible().Select(node => node.Label));
        Assert.All(tree.Roots, root => Assert.True(root.HasChildren));
        Assert.Empty(listed);

        tree.Roots[0].Toggle();
        Assert.Equal(["C:\\", "Apps", "Games", "music", "D:\\"], tree.Visible().Select(node => node.Label));
        Assert.Equal([0, 1, 1, 1, 0], tree.Visible().Select(node => node.Level));

        // A root with nothing in it shows as a leaf once looked at, and does not open.
        tree.Roots[1].Toggle();
        Assert.False(tree.Roots[1].HasChildren);
        Assert.False(tree.Roots[1].IsExpanded);

        tree.Roots[0].Toggle();
        Assert.Equal(["C:\\", "D:\\"], tree.Visible().Select(node => node.Label));
    }

    [Fact]
    public void ASecondClickOnTheSelectedFolderChoosesItAndClosesTheWindow()
    {
        var host = new WindowTestHost();
        string? chosen = null;
        var window = new DirectoryBrowserWindow("Pick one", path => chosen = path, FakeTree());
        host.Open(window);

        var c = window.Tree.Roots[0];
        window.ClickRow(host, c, onSprite: true);
        var music = window.Tree.Visible().Single(node => node.Label == "music");

        window.ClickRow(host, music, onSprite: false);
        Assert.Same(music, window.Selected);
        Assert.Null(chosen);

        window.ClickRow(host, music, onSprite: false);
        Assert.Equal("C:/music", chosen);
        Assert.DoesNotContain(window, host.Opened);
    }

    [Fact]
    public void TheSpriteColumnOpensAFolderAndTheRestOfTheRowSelectsIt()
    {
        var window = new DirectoryBrowserWindow("Pick one", _ => { }, FakeTree());
        window.Tree.Roots[0].Toggle();

        // Rows of 14 pixels from (5, 55); a level-1 row's sprite is 14 to 28 pixels in, its name after that.
        var apps = new Point(5 + 20, 55 + 14 + 3);
        Assert.Equal(("Apps", true), window.NodeAt(apps) is var (node, onSprite) ? (node.Label, onSprite) : default);
        Assert.Equal(("Apps", false), window.NodeAt(apps + new Point(30, 0)) is var (node2, onSprite2) ? (node2.Label, onSprite2) : default);
        Assert.Null(window.NodeAt(new Point(5 + 5, 55 + 14 + 3)));
        Assert.Null(window.NodeAt(new Point(5 + 20, 55 + (14 * 10))));
    }

    [Fact]
    public void AMusicFolderNeedsAFileTheMusicPlays()
    {
        Directory.CreateDirectory(_folder);
        Assert.Contains("contains no playable music files", SettingsDialog.MusicFolderProblem(_folder));
        Assert.Contains("is not a valid directory", SettingsDialog.MusicFolderProblem(Path.Combine(_folder, "missing")));

        File.WriteAllText(Path.Combine(_folder, "readme.txt"), "not music");
        Assert.NotNull(SettingsDialog.MusicFolderProblem(_folder));

        File.WriteAllBytes(Path.Combine(_folder, "song.OGG"), []);
        Assert.Null(SettingsDialog.MusicFolderProblem(_folder));
    }

    [Fact]
    public void TheSoundDialogsMusicFolderOpensTheBrowserAndAChosenFolderPlaysOwnMusic()
    {
        var host = new WindowTestHost();
        var dialog = new SettingsDialog(SettingsDialogKind.Sound, new GameConfig(), _ => { }, () => { });
        _ = dialog.Rows(host).ToList();

        var music = dialog.Settings().Single(setting => setting.Label == "Music");
        Assert.Equal("No folder location specified yet!", music.Value);
        music.Change(1);
        Assert.IsType<DirectoryBrowserWindow>(Assert.Single(host.Opened));

        // A folder without music is refused with the reason (two paragraphs: the message and the path).
        Directory.CreateDirectory(_folder);
        dialog.ChooseMusic(_folder);
        Assert.IsType<InformationWindow>(host.Opened[^1]);
        Assert.Equal((string.Empty, false), (dialog.Config.AudioMusic, dialog.Config.PlayOwnMusic));

        File.WriteAllBytes(Path.Combine(_folder, "song.mp3"), []);
        dialog.ChooseMusic(_folder);
        Assert.Equal((_folder, true), (dialog.Config.AudioMusic, dialog.Config.PlayOwnMusic));
        Assert.Equal(Path.GetFileName(_folder), dialog.Settings().Single(setting => setting.Label == "Music").Value);
    }
}
