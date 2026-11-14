using CorsixNet.Content.Settings;
using CorsixNet.Formats;

namespace CorsixNet.LuaConverter.Tests;

/// <summary>CorsixTH's Lua campaign, settings and hotkeys files turned into CorsixNet's YAML.</summary>
public class LuaFilesTests
{
    private const string Campaign = """
        --[[ Copyright (c) 2013 Someone

        Permission is hereby granted.--]]

        name = "Example campaign"

        description = "This is an example campaign where you get the chance to play " ..
          "through some of the levels shipped with CorsixTH. Happy gaming!"

        description_table = {
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
    public void ACampaignBecomesYamlWithItsLicence()
    {
        var yaml = LuaFiles.Campaign(Campaign);

        Assert.StartsWith("# Copyright (c) 2013 Someone\n#\n# Permission is hereby granted.\n", yaml, StringComparison.Ordinal);
        var english = CustomCampaign.ParseYaml(yaml);
        var french = CustomCampaign.ParseYaml(yaml, "fr");
        Assert.Equal("Example campaign", english.Name);
        Assert.StartsWith("This is an example campaign where you get the chance to play through", english.Description, StringComparison.Ordinal);
        Assert.Equal(["finisham.level", "avatar.level", "st.peter's.level"], english.Levels);
        Assert.Equal("Congratulations!", english.WinningText);
        Assert.True(english.Movie);
        Assert.Equal("Description française", french.Description);
        Assert.Equal("Toutes nos\nfélicitations!", french.WinningText!.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void APlainCampaignKeepsItsPlainTexts()
    {
        var plain = CustomCampaign.ParseYaml(LuaFiles.Campaign("""
            name = "Plain"
            description = "A " .. "plain" .. " description"
            levels = { "one.level" }
            winning_text = "Well done"
            """), "fr");

        Assert.Equal("A plain description", plain.Description);
        Assert.Equal("Well done", plain.WinningText);
        Assert.False(plain.Movie);
    }

    [Theory]
    [InlineData("name = os.exit()")]
    [InlineData("levels = { \"a.level\"")]
    [InlineData("name = \"unfinished")]
    [InlineData("print(\"hi\")")]
    public void RefusesWhatIsNotACampaignFile(string text) =>
        Assert.Throws<InvalidDataException>(() => LuaFiles.Campaign(text));

    [Fact]
    public void CorsixThsConfigTxtBecomesConfigYaml()
    {
        var yaml = LuaFiles.Config("""
            ------------------------- CorsixTH configuration file -------------------------
            -- Lines starting with two dashes (like this one) are ignored.
            language = [[German]]
            fullscreen = true
            width = 1024
            audio = true
            midi_api = nil -- [[alsa]]
            theme_hospital_install = [[D:\Games\Theme Hospital\data\Game]]
            unicode_fonts = { Russian = [[C:\Fonts\tahoma.ttf]] }
            """);

        var config = GameConfig.Parse(yaml);
        Assert.Equal("German", config.Language);
        Assert.True(config.Fullscreen);
        Assert.Equal(1024, config.Width);
        Assert.Equal(800, config.Height);
        Assert.Equal(@"D:\Games\Theme Hospital\data\Game", config.ThemeHospitalInstall);
        Assert.Equal(@"C:\Fonts\tahoma.ttf", config.LanguageFont(["Russian"]));
    }

    [Fact]
    public void CorsixThsHotkeysTxtBecomesHotkeysYaml()
    {
        var hotkeys = Hotkeys.Parse(LuaFiles.Hotkeys("""
            ingame_pause = {"ctrl", "q"}
            ingame_panel_bankManager = "B"
            """));

        Assert.Equal(new Hotkey("q", Ctrl: true), hotkeys["ingame_pause"]);
        Assert.Equal(new Hotkey("b"), hotkeys["ingame_panel_bankManager"]);
    }
}
