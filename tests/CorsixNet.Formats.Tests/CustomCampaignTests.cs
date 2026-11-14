namespace CorsixNet.Formats.Tests;

public class CustomCampaignTests
{
    [Fact]
    public void ReadsAYamlCampaignFile()
    {
        const string yaml = """
            name: Example campaign
            description:
              en: English description
              fr: Description française
            levels: [finisham.level, avatar.level, "st.peter's.level"]
            winning_text: Congratulations!
            movie: true
            """;

        var campaign = CustomCampaign.ParseYaml(yaml);
        var french = CustomCampaign.ParseYaml(yaml, "fr");
        var russian = CustomCampaign.ParseYaml(yaml, "ru");

        Assert.Equal("Example campaign", campaign.Name);
        Assert.Equal("English description", campaign.Description);
        Assert.Equal(["finisham.level", "avatar.level", "st.peter's.level"], campaign.Levels);
        Assert.Equal("Congratulations!", campaign.WinningText);
        Assert.True(campaign.Movie);
        Assert.Equal("Description française", french.Description);
        Assert.Equal("English description", russian.Description);
        Assert.False(CustomCampaign.ParseYaml("levels: [a.level]").Movie);
    }

    [Theory]
    [InlineData("levels: [a.level")]
    [InlineData("- just\n- a list")]
    public void RefusesWhatIsNotAYamlCampaignFile(string text)
    {
        Assert.Throws<InvalidDataException>(() => CustomCampaign.ParseYaml(text));
    }
}
