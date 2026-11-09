using System.CommandLine;
using CorsixNet.Client;
using CorsixNet.Client.Maps;
using CorsixNet.Content.Interface;
using CorsixNet.Sim.Economy;

namespace CorsixNet.Client.Tests;

public sealed class ClientCommandLineTests
{
    private static ClientOptions Bind(params string[] args)
    {
        var result = ClientCommandLine.Parse(args);
        Assert.Empty(result.Errors);
        Assert.Null(result.Action);
        return ClientCommandLine.Bind(result);
    }

    private static void AssertError(params string[] args) => Assert.NotEmpty(ClientCommandLine.Parse(args).Errors);

    [Fact]
    public void No_arguments_give_the_defaults()
    {
        var options = Bind();

        Assert.Equal(new ClientOptions(), options);
        Assert.Equal(1f, options.Zoom);
        Assert.Equal(MapOverlay.None, options.Overlay);
        Assert.Equal(1, options.Upscale);
        Assert.Equal(GameSpeed.Normal, options.Speed);
        Assert.Equal(Difficulty.Medium, options.Difficulty);
        Assert.Null(options.HostPort);
    }

    [Fact]
    public void Every_option_is_read()
    {
        var options = Bind(
            @"C:\Games\Theme Hospital",
            "--level", "LEVEL.L3",
            "--map", "a.map",
            "--campaign", "c.campaign",
            "--difficulty", "hard",
            "--zoom", "2.5",
            "--overlay", "parcels",
            "--demo",
            "--speed", "maxspeed",
            "--upscale", "4",
            "--continue",
            "--public",
            "--connect", "example.org:1234",
            "--name", "Dana");

        Assert.Equal(@"C:\Games\Theme Hospital", options.GamePath);
        Assert.Equal("LEVEL.L3", options.InitialLevel);
        Assert.Equal("a.map", options.MapPath);
        Assert.Equal("c.campaign", options.CampaignPath);
        Assert.Equal(Difficulty.Hard, options.Difficulty);
        Assert.Equal(2.5f, options.Zoom);
        Assert.Equal(MapOverlay.Parcels, options.Overlay);
        Assert.True(options.Demo);
        Assert.Equal(GameSpeed.MaxSpeed, options.Speed);
        Assert.Equal(4, options.Upscale);
        Assert.True(options.ContinueGame);
        Assert.True(options.PublicHost);
        Assert.Equal("example.org:1234", options.ConnectTo);
        Assert.Equal("Dana", options.PlayerName);
        Assert.False(options.SpriteViewer);
    }

    [Fact]
    public void File_options_become_full_paths()
    {
        var options = Bind("--load", "save.sav", "--capture", "shot.png", "--config", "config.txt");

        Assert.Equal(Path.GetFullPath("save.sav"), options.LoadPath);
        Assert.Equal(Path.GetFullPath("shot.png"), options.CapturePath);
        Assert.Equal(Path.GetFullPath("config.txt"), options.ConfigPath);
    }

    [Fact]
    public void A_sheet_opens_the_sprite_viewer()
    {
        Assert.True(Bind("--sprite-viewer").SpriteViewer);

        var options = Bind("--sheet", "DATA/VBLK-0");
        Assert.True(options.SpriteViewer);
        Assert.Equal("DATA/VBLK-0", options.InitialSheet);
    }

    [Fact]
    public void The_host_port_is_optional()
    {
        Assert.Equal(ClientCommandLine.DefaultPort, Bind("--host").HostPort);
        Assert.Equal(9000, Bind("--host", "9000").HostPort);

        var options = Bind("--host", "Games", "--public");
        Assert.Equal(ClientCommandLine.DefaultPort, options.HostPort);
        Assert.Equal("Games", options.GamePath);
        Assert.True(options.PublicHost);
    }

    [Theory]
    [InlineData("--zoom", "big")]
    [InlineData("--upscale", "3")]
    [InlineData("--speed", "SpeedUp")]
    [InlineData("--speed", "fast")]
    [InlineData("--overlay", "nothing")]
    [InlineData("--difficulty", "brutal")]
    [InlineData("--level")]
    [InlineData("--unknown")]
    public void Mistakes_are_errors(params string[] args) => AssertError(args);

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Help_lists_the_options(string option)
    {
        var output = new StringWriter();
        var exitCode = ClientCommandLine.Parse([option]).Invoke(new InvocationConfiguration { Output = output });

        Assert.Equal(0, exitCode);
        var help = output.ToString();
        foreach (var name in new[] { "--demo", "--load", "--continue", "--host", "--connect", "--sprite-viewer", "--difficulty" })
        {
            Assert.Contains(name, help, StringComparison.Ordinal);
        }
    }
}
