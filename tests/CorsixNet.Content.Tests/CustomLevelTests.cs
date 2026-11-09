using CorsixNet.Content.Levels;
using CorsixNet.Formats;
using CorsixNet.Sim.Economy;

namespace CorsixNet.Content.Tests;

/// <summary>CorsixTH custom levels: a <c>.level</c> file with its <c>.map</c>.</summary>
public sealed class CustomLevelTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("corsixnet-level-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void LoadsTheMapBesideTheLevelWithTheLevelsSettings()
    {
        File.WriteAllBytes(Path.Combine(_folder, "Island.MAP"), new MapFileBuilder().Camera(0, 5, 6).Build());
        var levelPath = Write("island.level", """
            %Name = "Island"
            %MapFile = "island.map"
            %LevelBriefing = "Cure them all."
            #town.StartCash.InterestRate 20000 300
            #gbv.LandCostPerTile 30
            """);

        var (level, info) = LevelMapLoader.LoadCustom(levelPath);

        Assert.Equal("Island", info.Name);
        Assert.Equal("Cure them all.", info.Briefing);
        Assert.Equal(new Sim.Maps.TilePosition(5, 6), level.Map.GetCameraTile(0));
        var rules = level.Rules!;
        Assert.Equal(20000, rules.StartCash);
        Assert.Equal(300, rules.InterestRate);
        Assert.Equal(30, rules.LandCostPerTile);
        Assert.Equal(Difficulty.Medium, rules.Difficulty);
        Assert.Null(rules.LevelNumber);

        // --map takes a level file too.
        Assert.Equal(20000, LevelMapLoader.LoadFile(levelPath).Rules!.StartCash);
    }

    [Fact]
    public void ALevelWithoutAMapFileOrWithAMissingOneIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => LevelMapLoader.LoadCustom(Write("none.level", "%Name = \"None\"")));
        Assert.Throws<FileNotFoundException>(() => LevelMapLoader.LoadCustom(Write("lost.level", "%MapFile = \"lost.map\"")));
    }

    [Theory]
    [InlineData("#overlay.difficulty full")]
    [InlineData("#overlay.level_number 5")]
    [InlineData("#overlay.difficulty.level_number medium 5")]
    [InlineData("#overlay.difficulty.level_number full 13")]
    public void AnIncompleteOrWrongOverlayIsRefused(string overlay)
    {
        Assert.Throws<InvalidDataException>(() => LevelRulesLoader.LoadCustom(LevelConfig.Parse(System.Text.Encoding.ASCII.GetBytes(overlay)), null, BaseGameContent.Create()));
    }

    [Fact]
    public void AnOverlayNeedsTheOriginalGame()
    {
        var config = LevelConfig.Parse("#overlay.difficulty.level_number hard 5"u8);

        Assert.Throws<InvalidOperationException>(() => LevelRulesLoader.LoadCustom(config, null, BaseGameContent.Create()));
    }

    [Fact]
    public void AnOverlayReadsTheCampaignLevelOnTopAtItsDifficulty()
    {
        var install = Formats.Tests.RealGameData.Require();
        var config = LevelConfig.Parse("""
            #overlay.difficulty.level_number hard 5
            #town.StartCash 1
            """u8);

        var rules = LevelRulesLoader.LoadCustom(config, install, BaseGameContent.Create());

        Assert.Equal(Difficulty.Hard, rules.Difficulty);
        Assert.Equal(3, rules.Staff.StartStaff.Count);
        Assert.NotEqual(1, rules.StartCash);
    }

    [Fact]
    public void ACampaignListsItsLevelsBesideIt()
    {
        File.WriteAllBytes(Path.Combine(_folder, "one.map"), new MapFileBuilder().Build());
        Write("One.level", "%MapFile = \"one.map\"\n#town.StartCash 1234");
        Write("two.level", "%MapFile = \"one.map\"");
        var campaignPath = Write("test.campaign", """
            name = "Test"
            levels = { "one.level", "two.level" }
            """);

        var (campaign, paths) = LevelMapLoader.LoadCampaign(campaignPath);

        Assert.Equal("Test", campaign.Name);
        Assert.Equal(["One.level", "two.level"], paths.Select(Path.GetFileName));
        Assert.Equal(1234, LevelMapLoader.LoadFile(paths[0]).Rules!.StartCash);
        Assert.Throws<FileNotFoundException>(() => LevelMapLoader.LoadCampaign(Write("lost.campaign", "levels = { \"three.level\" }")));
        Assert.Throws<InvalidDataException>(() => LevelMapLoader.LoadCampaign(Write("empty.campaign", "name = \"Empty\"")));
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        return path;
    }
}
