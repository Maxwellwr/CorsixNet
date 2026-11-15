using CorsixNet.Client.Ui.Windows;
using CorsixNet.Content.Settings;

namespace CorsixNet.Client.Tests;

public sealed class FolderSettingsWindowTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "CorsixNetFolderTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void EachFolderOpensTheBrowserAndXPutsItBack()
    {
        var host = new WindowTestHost();
        GameConfig? applied = null;
        var window = new FolderSettingsWindow(new GameConfig(), config => applied = config, () => { }, "C:/Default/Saves", "C:/Default/Screenshots");

        // The saves row shows the default until one is chosen; its button opens the folder browser.
        host.Click(window, "C:/Default/Saves");
        Assert.IsType<DirectoryBrowserWindow>(Assert.Single(host.Opened));

        // The browser's choice is saved at once.
        window.ChooseSaves(_folder);
        Assert.Equal(_folder, applied!.Savegames);
        Assert.Contains(window.Rows(host).SelectMany(row => row.Cells), cell => cell.Text == _folder);

        host.Click(window, "X");
        Assert.Equal(string.Empty, applied.Savegames);
    }

    [Fact]
    public void AFolderWithoutTheGamesDataIsRefusedAndNothingRestarts()
    {
        var host = new WindowTestHost();
        var restarted = false;
        GameConfig? applied = null;
        var window = new FolderSettingsWindow(new GameConfig(), config => applied = config, () => { }, "saves", "shots", () => restarted = true);
        _ = window.Rows(host).ToList();
        Directory.CreateDirectory(_folder);

        window.ChooseData(_folder);

        Assert.IsType<InformationWindow>(Assert.Single(host.Opened));
        Assert.Null(applied);
        Assert.False(restarted);
    }

    [Fact]
    public void AMusicFolderWithMusicIsTakenAndPlaysOwnMusic()
    {
        var host = new WindowTestHost();
        GameConfig? applied = null;
        var window = new FolderSettingsWindow(new GameConfig(), config => applied = config, () => { }, "saves", "shots");
        _ = window.Rows(host).ToList();
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(Path.Combine(_folder, "song.flac"), []);

        window.ChooseMusic(_folder);

        Assert.Equal((_folder, true), (applied!.AudioMusic, applied.PlayOwnMusic));
    }
}
