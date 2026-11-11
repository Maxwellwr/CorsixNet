using CorsixNet.Content.Settings;

namespace CorsixNet.Content.Tests;

public sealed class HotkeysTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "CorsixNetHotkeysTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void ReadsKeysAndModifiersAndKeepsTheDefaultsForTheRest()
    {
        var hotkeys = Hotkeys.Parse("""
            -- My keys.
            ingame_panel_bankManager = "B"
            ingame_panel_adviserHistory = {"alt", "shift", "m"}
            ingame_pause = "space"
            """);

        Assert.Equal(new Hotkey("b"), hotkeys["ingame_panel_bankManager"]);
        Assert.Equal(new Hotkey("m", Shift: true, Alt: true), hotkeys["ingame_panel_adviserHistory"]);
        Assert.Equal(new Hotkey("space"), hotkeys["ingame_pause"]);
        Assert.Equal(new Hotkey("f2"), hotkeys["ingame_panel_bankStats"]);
        Assert.Equal(new Hotkey("f3", Shift: true), hotkeys["ingame_panel_staffManage"]);
    }

    [Fact]
    public void IgnoresUnknownActionsAndValuesThatAreNotKeys()
    {
        var hotkeys = Hotkeys.Parse("""
            ingame_panel_casebook = "printscreen"
            ingame_panel_research = {"hyper", "r"}
            ingame_panel_policy = 9
            ingame_poopLog = "f12"
            """);

        Assert.Equal(new Hotkey("f5"), hotkeys["ingame_panel_casebook"]);
        Assert.Equal(new Hotkey("f6"), hotkeys["ingame_panel_research"]);
        Assert.Equal(new Hotkey("f9"), hotkeys["ingame_panel_policy"]);
        Assert.Null(hotkeys["ingame_poopLog"]);
    }

    [Fact]
    public void RejectsTextThatIsNotAHotkeysFile() =>
        Assert.Throws<InvalidDataException>(() => Hotkeys.Parse("print('hello')"));

    [Fact]
    public void WritesTheDefaultsWhenThereIsNoFileAndReadsThemBack()
    {
        var path = Path.Combine(_folder, Hotkeys.FileName);

        var first = Hotkeys.Load(path);

        Assert.True(File.Exists(path));
        var again = Hotkeys.Load(path);
        foreach (var (name, key) in Hotkeys.Defaults)
        {
            Assert.Equal(key, first[name]);
            Assert.Equal(key, again[name]);
        }
    }

    [Fact]
    public void TheCameraAndSpeedKeysAreCorsixThsAndCanBeChanged()
    {
        var hotkeys = Hotkeys.Parse("""
            ingame_zoom_in = "up"
            ingame_recallPosition_7 = {"shift", "7"}
            ingame_gamespeed_normal = "f12"
            """);

        Assert.Equal(new Hotkey("up"), hotkeys["ingame_zoom_in"]);
        Assert.Equal(new Hotkey("7", Shift: true), hotkeys["ingame_recallPosition_7"]);
        Assert.Equal(new Hotkey("f12"), hotkeys["ingame_gamespeed_normal"]);
        Assert.Equal(new Hotkey("=", Shift: true), hotkeys["ingame_zoom_in_more"]);
        Assert.Equal(new Hotkey("0", Alt: true), hotkeys["ingame_storePosition_0"]);
        Assert.Equal(new Hotkey("left"), hotkeys["ingame_scroll_left"]);
        Assert.Equal(new Hotkey("b"), hotkeys["ingame_panel_buildRoom"]);
    }

    [Fact]
    public void ARewrittenFileKeepsTheChangedKeys()
    {
        var hotkeys = Hotkeys.Parse("""ingame_quickSave = {"ctrl", "s"}""");

        var reread = Hotkeys.Parse(hotkeys.ToText());

        Assert.Equal(new Hotkey("s", Ctrl: true), reread["ingame_quickSave"]);
        Assert.Contains("""ingame_quickSave = {"ctrl", "s"}""", hotkeys.ToText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("f1", true)]
    [InlineData("F12", true)]
    [InlineData("f13", false)]
    [InlineData("a", true)]
    [InlineData("7", true)]
    [InlineData("=", true)]
    [InlineData("escape", true)]
    [InlineData("ctrl", false)]
    [InlineData("", false)]
    public void KnowsTheKeyNames(string name, bool known) => Assert.Equal(known, Hotkeys.IsKeyName(name));

    [Fact]
    public void ShowsKeysAsThePlayerReadsThem()
    {
        Assert.Equal("F1", new Hotkey("f1").ToString());
        Assert.Equal("Ctrl+H", new Hotkey("h", Ctrl: true).ToString());
        Assert.Equal("Shift+Alt+S", new Hotkey("s", Shift: true, Alt: true).ToString());
    }
}
