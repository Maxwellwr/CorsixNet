namespace CorsixNet.Formats.Tests;

public class CustomCampaignTests
{
    private const string Example = """
        --[[ Copyright (c) 2013 Someone

        Permission is hereby granted.--]]

        name = "Example campaign"

        description = "This is an example campaign where you get the chance to play " ..
          "through some of the levels shipped with CorsixTH. Happy gaming!"

        description_table = {
          en = "English description",
          fr = "Description " .. 'française',
        }

        movie = true -- play the advance movies

        levels = {
          "finisham.level",
          "avatar.level";
          "st.peter's.level",
        }

        winning_text_table = {}
        winning_text_table.en = "Congratulations!"
        winning_text_table["fr"] = [[Toutes nos
        félicitations!]]
        """;

    [Fact]
    public void ReadsACampaignFile()
    {
        var campaign = CustomCampaign.Parse(Example);

        Assert.Equal("Example campaign", campaign.Name);
        Assert.Equal("English description", campaign.Description);
        Assert.Equal(["finisham.level", "avatar.level", "st.peter's.level"], campaign.Levels);
        Assert.Equal("Congratulations!", campaign.WinningText);
        Assert.True(campaign.Movie);
    }

    [Fact]
    public void PicksTheLanguageThenEnglishThenThePlainText()
    {
        var french = CustomCampaign.Parse(Example, "fr");
        var plain = CustomCampaign.Parse("""
            name = "Plain"
            description = "A " .. "plain" .. " description"
            levels = { "one.level" }
            winning_text = "Well done"
            """, "fr");

        Assert.Equal("Description française", french.Description);
        Assert.Equal("Toutes nos\nfélicitations!", french.WinningText!.ReplaceLineEndings("\n"));
        Assert.Equal("A plain description", plain.Description);
        Assert.Equal("Well done", plain.WinningText);
        Assert.False(plain.Movie);
    }

    [Theory]
    [InlineData("name = os.exit()")]
    [InlineData("levels = { \"a.level\"")]
    [InlineData("name = \"unfinished")]
    [InlineData("print(\"hi\")")]
    public void RefusesWhatIsNotACampaignFile(string text)
    {
        Assert.Throws<InvalidDataException>(() => CustomCampaign.Parse(text));
    }
}
