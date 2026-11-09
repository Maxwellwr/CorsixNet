namespace CorsixNet.Formats.Tests;

public class CustomLevelInfoTests
{
    [Fact]
    public void ReadsTheGeneralInformation()
    {
        var info = CustomLevelInfo.Parse("""
            ---------------------- General Information -------------------------
            %Name = "Avatar"
            %MapFile = "avatar.map"

            %LevelBriefing = "In this town, there are no doctors.
            Try to earn 50.000."

            %LevelDebriefingTable.en = "Good work!"
            %LevelDebriefing = "Well done."
            #town.StartCash.InterestRate 20000 300
            """);

        Assert.Equal("Avatar", info.Name);
        Assert.Equal("avatar.map", info.MapFile);
        Assert.Equal("In this town, there are no doctors.\nTry to earn 50.000.", info.Briefing!.ReplaceLineEndings("\n"));
        Assert.Equal("Good work!", info.Debriefing);
    }

    [Fact]
    public void FallsBackToTheDeprecatedLevelFileAndAnUnknownName()
    {
        var info = CustomLevelInfo.Parse("%LevelFile=\"old.map\"");

        Assert.Equal("Unknown name", info.Name);
        Assert.Equal("old.map", info.MapFile);
        Assert.Null(info.Briefing);
        Assert.Null(CustomLevelInfo.Parse(string.Empty).MapFile);
    }
}
