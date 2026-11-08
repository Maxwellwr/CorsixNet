using System.Text;
using CorsixNet.Content.Levels;
using CorsixNet.Formats;
using CorsixNet.Formats.Tests;

namespace CorsixNet.Content.Tests;

public class LevelRulesLoaderTests
{
    [Theory]
    [InlineData("LEVEL.L1", 1)]
    [InlineData("level.l12", 12)]
    [InlineData(@"C:\Games\LEVELS\LEVEL.L20", 20)]
    [InlineData("custom.map", null)]
    [InlineData("LEVEL.L", null)]
    public void RecognisesOriginalLevelMaps(string fileName, int? expected)
    {
        Assert.Equal(expected, LevelRulesLoader.LevelNumber(fileName));
    }

    [Fact]
    public void TakesCostsByConfigNumberAndTheLevelsOwnTownFirst()
    {
        var config = LevelConfig.Parse(Encoding.ASCII.GetBytes("""
            #town.StartCash 10000
            #towns[3].StartCash 50000
            #rooms[7].Cost 2280
            #objects[2].StartCost 100
            #objects[1].StartCost 300
            """));
        var content = BaseGameContent.Create();

        var rules = LevelRulesLoader.FromConfig(config, 3, content);

        Assert.Equal(50000, rules.StartCash);
        Assert.Equal(2280, rules.RoomCost("gp"));
        Assert.Equal(100, rules.ObjectCost("cabinet"));
        Assert.Equal(10000, LevelRulesLoader.FromConfig(config, 4, content).StartCash);
        Assert.Equal(10000, LevelRulesLoader.FromConfig(config, null, content).StartCash);
        Assert.Equal(LevelRulesLoader.DefaultStartCash, LevelRulesLoader.FromConfig(LevelConfig.Parse([]), 1, content).StartCash);
    }

    [Fact]
    public void ReadsTheOriginalLevelOneMoney()
    {
        var install = RealGameData.Require();
        var content = BaseGameContent.Create();

        var rules = LevelRulesLoader.LoadOriginal(install, "LEVEL.L1", content);

        Assert.Equal(40000, rules.StartCash);
        Assert.Equal(2280, rules.RoomCost("gp"));
        Assert.Equal(100, rules.ObjectCost("cabinet"));
        Assert.Equal(20, rules.ObjectCost("chair"));
        Assert.True(rules.ObjectCost("desk") > 0);
        Assert.Equal(2280 + rules.ObjectCost("desk") + 100 + 20, rules.BuildCost(BaseGameContent.GpOffice));
    }

    [Fact]
    public void StartsOriginalLevelsWithTheirMoney()
    {
        var install = RealGameData.Require();

        var simulation = LevelMapLoader.LoadOriginal(install, "LEVEL.L1").StartSimulation(seed: 1);

        Assert.Equal(40000, simulation.Hospitals[0].Balance);
        Assert.Equal(2280, simulation.Rules.RoomCost("gp"));
    }
}
