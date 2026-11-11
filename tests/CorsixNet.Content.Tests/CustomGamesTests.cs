using CorsixNet.Content.Levels;
using CorsixNet.Content.Saves;
using CorsixNet.Content.Settings;

namespace CorsixNet.Content.Tests;

/// <summary>The main menu's custom levels and campaigns, Continue Game and the user folders (CorsixTH's main_menu.lua and file_browsers).</summary>
public sealed class CustomGamesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "corsixnet-custom-" + Guid.NewGuid().ToString("N"));

    public CustomGamesTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void FindsLevelsByNameInSubfoldersToo()
    {
        Write("b.level", "%Name = \"Zebra Town\"\n%LevelBriefing = \"Stripes.\"\n");
        Write("more/a.level", "%Name = \"Avatar\"\n");
        Write("notes.txt", "%Name = \"Not a level\"\n");

        var levels = CustomGames.FindLevels(_folder, Path.Combine(_folder, "missing"));

        Assert.Equal(["Avatar", "Zebra Town"], levels.Select(level => level.Info.Name));
        Assert.Equal("Stripes.", levels[1].Info.Briefing);
        Assert.EndsWith("a.level", levels[0].Path, StringComparison.Ordinal);
    }

    [Fact]
    public void FindsCampaignsLeavingOutEmptyOnesAndSecondsOfAName()
    {
        Write("one.campaign", "name = \"Example\"\ndescription = \"First.\"\nlevels = { \"a.level\" }\n");
        Write("two/one.campaign", "name = \"Example\"\nlevels = { \"b.level\" }\n");
        Write("empty.campaign", "name = \"Empty\"\nlevels = {}\n");
        Write("other.campaign", "levels = { \"c.level\" }\n");

        var campaigns = CustomGames.FindCampaigns(_folder);

        Assert.Equal(["Example", null], campaigns.Select(campaign => campaign.Campaign.Name));
        Assert.Equal("First.", campaigns[0].Campaign.Description);
    }

    [Fact]
    public void ContinueTakesTheNewestSaveAutosavesIncluded()
    {
        var store = new SaveGameStore(_folder);
        Assert.Null(new SaveGameStore(Path.Combine(_folder, "none")).MostRecentSave());
        var manual = Write("mine.sav", "x");
        var autosave = Write(Path.Combine(SaveGameStore.AutosaveFolder, "Autosave-01-01.sav"), "x");
        File.SetLastWriteTimeUtc(manual, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(autosave, new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(autosave, store.MostRecentSave());
    }

    [Fact]
    public void TheUserFoldersDefaultBesideTheSavesFolder()
    {
        var saves = Path.Combine(_folder, "Saves");

        Assert.Equal(Path.Combine(_folder, "Levels"), GameConfig.UserFolder(string.Empty, saves, "Levels"));
        Assert.Equal("D:/Mine", GameConfig.UserFolder("D:/Mine", saves, "Levels"));
        var config = GameConfig.Parse(new GameConfig { Levels = "L", Campaigns = "C" }.ToText());
        Assert.Equal(("L", "C"), (config.Levels, config.Campaigns));
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }
}
