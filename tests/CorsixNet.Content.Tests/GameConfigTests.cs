using CorsixNet.Content.Settings;
using CorsixNet.Formats;

namespace CorsixNet.Content.Tests;

public sealed class GameConfigTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "CorsixNetConfigTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void ReadsCorsixThsSettingsAndIgnoresTheOthers()
    {
        var config = GameConfig.Parse("""
            ------------------------- CorsixTH configuration file -------------------------
            -- Lines starting with two dashes (like this one) are ignored.
            language = [[German]]
            fullscreen = true
            width = 1024
            height = 768
            audio = true
            sound_volume = 0.25
            play_sounds = false
            prevent_edge_scrolling = true
            scrolling_momentum = 0.5
            zoom_speed = 120
            scroll_speed = 3
            shift_scroll_speed = 6.5
            midi_api = nil -- [[alsa]]
            enable_screen_shake = false
            autosave_frequency = 3
            theme_hospital_install = [[D:\Games\Theme Hospital\data\Game]]
            player_name = [[Maxim]]
            free_build_mode = true
            """);

        Assert.Equal("German", config.Language);
        Assert.Equal(StringTable.German, config.OriginalLanguageNumber());
        Assert.True(config.Fullscreen);
        Assert.Equal(1024, config.Width);
        Assert.Equal(768, config.Height);
        Assert.False(config.EnableScreenShake);
        Assert.Equal(3, config.AutosaveFrequency);
        Assert.Equal(@"D:\Games\Theme Hospital\data\Game", config.ThemeHospitalInstall);
        Assert.Equal("Maxim", config.PlayerName);
        Assert.True(config.FreeBuildMode);
        Assert.False(config.PlaySounds);
        Assert.Equal(0.25m, config.SoundVolume);
        Assert.Equal(new(3, 6.5, 120, 0.5, true), config.ScrollSettings());
    }

    [Fact]
    public void KeepsTheDefaultsForMissingSettingsAndValuesOfTheWrongType()
    {
        var config = GameConfig.Parse("width = [[wide]]\nheight = 600.5\nfullscreen = 1\nsound_volume = [[loud]]\n");

        Assert.Equal(new GameConfig(), config);
        Assert.Equal("English", config.Language);
        Assert.Equal(GameConfig.PlaceholderInstall, config.ThemeHospitalInstall);
        Assert.Equal(1, config.AutosaveFrequency);
        Assert.True(config.EnableScreenShake);
    }

    [Fact]
    public void RefusesAFileThatIsNotASettingsFile()
    {
        Assert.Throws<InvalidDataException>(() => GameConfig.Parse("print('hello')"));
    }

    [Fact]
    public void WritesWhatItReads()
    {
        var config = new GameConfig
        {
            Language = "fr",
            Fullscreen = true,
            Width = 1920,
            Height = 1080,
            EnableScreenShake = false,
            AutosaveFrequency = 2,
            FreeBuildMode = true,
            PlaySounds = false,
            SoundVolume = 0.75m,
            PreventEdgeScrolling = true,
            ScrollingMomentum = 0.9m,
            ZoomSpeed = 200,
            ScrollSpeed = 1.5m,
            ShiftScrollSpeed = 8,
            ThemeHospitalInstall = @"C:\Games\Theme Hospital",
            UnicodeFont = @"C:\Windows\Fonts\arial.ttf",
            PlayerName = "Name with ]] and ]=] in it]",
        };

        Assert.Equal(config, GameConfig.Parse(config.ToText()));
    }

    [Fact]
    public void WritesTheDefaultsWhenThereIsNoFile()
    {
        var path = Path.Combine(_folder, "CorsixNet", GameConfig.FileName);

        var config = GameConfig.Load(path);

        Assert.Equal(new GameConfig(), config);
        Assert.True(File.Exists(path));
        Assert.Equal(config, GameConfig.Parse(File.ReadAllText(path)));

        File.WriteAllText(path, "width = 900");
        Assert.Equal(900, GameConfig.Load(path).Width);
    }

    [Theory]
    [InlineData("English", StringTable.English)]
    [InlineData("eng", StringTable.English)]
    [InlineData(" Français ", StringTable.French)]
    [InlineData("de", StringTable.German)]
    [InlineData("ITA", StringTable.Italian)]
    [InlineData("spa", StringTable.Spanish)]
    [InlineData("Svenska", StringTable.Swedish)]
    [InlineData("Russian", null)]
    public void MapsLanguagesToTheOriginalsStringFiles(string language, int? number)
    {
        Assert.Equal(number, new GameConfig { Language = language }.OriginalLanguageNumber());
    }
}
