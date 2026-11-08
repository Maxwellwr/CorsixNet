using System.Text;

namespace CorsixNet.Formats.Tests;

public class LevelConfigTests
{
    [Fact]
    public void ReadsSeveralFieldsPerLineAndIgnoresComments()
    {
        var config = Parse("""
            Level One (19-02-97)
            #towns[1].StartCash.IllRate.InterestRate	40000	2	100	Level 1
            #rooms[7].Cost		2280		GP_OFFICE
            not a setting #rooms[7].Cost 1
            """);

        Assert.Equal(40000, config.GetInt("towns[1].StartCash"));
        Assert.Equal(2, config.GetInt("towns[1].IllRate"));
        Assert.Equal(100, config.GetInt("towns[1].InterestRate"));
        Assert.Equal(2280, config.GetInt("rooms[7].Cost"));
        Assert.Null(config.GetInt("towns[1].Level"));
        Assert.Equal(4, config.Count);
    }

    [Fact]
    public void ALineWithoutFieldsSetsValue()
    {
        var config = Parse("#visuals[0]\t\t5\tI_BLOATY_HEAD\r\n#lose_criteria[1].Value\t-20000\r\n");

        Assert.Equal(5, config.GetInt("visuals[0].Value"));
        Assert.Equal(-20000, config.GetInt("lose_criteria[1].Value"));
    }

    [Fact]
    public void LaterFilesOverrideEarlierOnes()
    {
        var config = Parse("#rooms[7].Cost 2280\n#gbv.Salary 10\n");

        config.Apply(Encoding.ASCII.GetBytes("#rooms[7].Cost 3000\n"));

        Assert.Equal(3000, config.GetInt("rooms[7].Cost"));
        Assert.Equal(10, config.GetInt("gbv.Salary"));
        Assert.Equal("10", config["gbv.Salary"]);
        Assert.Null(config["gbv.Missing"]);
    }

    [Fact]
    public void ReadsTheOriginalLevelOneSettings()
    {
        var install = RealGameData.Require();

        var config = LevelConfig.Parse(install.ReadDataFile("LEVELS", "FULL00.SAM"));
        config.Apply(install.ReadDataFile("LEVELS", "FULL01.SAM"));

        Assert.Equal(40000, config.GetInt("towns[1].StartCash"));
        Assert.Equal(2280, config.GetInt("rooms[7].Cost"));
        Assert.Equal(100, config.GetInt("objects[2].StartCost"));
        Assert.Equal(1, config.GetInt("objects[9].AvailableForLevel"));
    }

    private static LevelConfig Parse(string text) => LevelConfig.Parse(Encoding.ASCII.GetBytes(text));
}
