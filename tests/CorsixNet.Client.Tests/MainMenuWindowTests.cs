using CorsixNet.Client.Ui.Windows;
using CorsixNet.Content.Levels;
using CorsixNet.Formats;

namespace CorsixNet.Client.Tests;

/// <summary>The main menu and its custom game lists (CorsixTH's main_menu.lua, custom_game.lua, custom_campaign.lua).</summary>
public class MainMenuWindowTests
{
    [Fact]
    public void TheMainMenuStaysAndPassesOnItsItems()
    {
        var host = new WindowTestHost();
        var actions = new List<string>();
        var menu = new MainMenuWindow(actions.Add, "1.2.3");

        foreach (var item in (string[])["Campaign", "Custom Campaign", "Single Scenario", "Continue Game", "Load Game", "Settings", "Map Editor", "Exit"])
        {
            host.Click(menu, item);
        }

        Assert.True(menu.IsPinned);
        Assert.Equal(["newgame", "campaign", "scenario", "continue", "load", "options", "mapeditor", "exit"], actions);
        Assert.Contains("Version: 1.2.3", host.Lines(menu));
    }

    [Fact]
    public void TheMainMenuAndTheNewGameWindowSpeakTheChosenTranslation()
    {
        var languages = CorsixNet.Content.Languages.CorsixLanguages.Load(Path.Combine(AppContext.BaseDirectory, "Languages"));
        var german = languages.Load("German", _ => null);
        var host = new WindowTestHost { Texts = new CorsixNet.Content.OriginalTexts(StringTable.Parse(new byte[] { 0, 0 }), german) };
        var actions = new List<string>();
        var menu = new MainMenuWindow(actions.Add, "1.2.3");

        host.Click(menu, german.Get("main_menu.new_game")!);
        Assert.Equal(["newgame"], actions);
        Assert.Contains(german.Get("main_menu.version") + "1.2.3", host.Lines(menu));

        var newGame = new NewGameWindow([CorsixNet.Sim.Economy.Difficulty.Medium], "Max", (_, _) => { });
        Assert.Contains(host.Lines(newGame), line => line.Contains(german.Get("new_game_window.medium")!, StringComparison.Ordinal));
    }

    [Fact]
    public void ACustomLevelStartsWithFreeBuildIfTicked()
    {
        var host = new WindowTestHost();
        var levels = new[]
        {
            new CustomLevelEntry("a.level", new CustomLevelInfo("Avatar", null, "Build a hospital.", null)),
            new CustomLevelEntry("z.level", new CustomLevelInfo("Zebra", null, null, null)),
        };
        (CustomLevelEntry Level, bool FreeBuild)? started = null;
        var window = new CustomLevelWindow(levels, freeBuild: false, (level, free) => started = (level, free));
        host.Opened.Add(window);

        Assert.Contains("Build a hospital.", host.Lines(window));
        host.Click(window, "Zebra");
        host.Click(window, "Free Build");
        host.Click(window, "Start");

        Assert.Equal((levels[1], true), started);
        Assert.Empty(host.Opened);
    }

    [Fact]
    public void ACustomCampaignStartsTheOneChosen()
    {
        var host = new WindowTestHost();
        var campaigns = new[]
        {
            new CustomCampaignEntry("x/first.campaign", new CustomCampaign(null, "The first.", ["a.level"], null, false)),
            new CustomCampaignEntry("x/second.campaign", new CustomCampaign("Second", null, ["b.level"], null, false)),
        };
        CustomCampaignEntry? started = null;
        var window = new CustomCampaignWindow(campaigns, campaign => started = campaign);

        Assert.Contains("The first.", host.Lines(window));
        host.Click(window, "first");
        host.Click(window, "Second");
        host.Click(window, "Start campaign");

        Assert.Same(campaigns[1], started);
    }

    [Fact]
    public void EmptyListsSayWhereToPutTheFiles()
    {
        var host = new WindowTestHost();

        Assert.Contains(host.Lines(new CustomLevelWindow([], false, (_, _) => { })), line => line.Contains("levels folder", StringComparison.Ordinal));
        Assert.Contains(new CustomCampaignWindow([], _ => { }).Rows(host).SelectMany(row => row.Cells), cell => cell.Text == "Start campaign" && cell.OnClick is null);
    }
}
