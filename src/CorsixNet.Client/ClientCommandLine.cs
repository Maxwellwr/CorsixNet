using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using CorsixNet.Client.Maps;
using CorsixNet.Content.Interface;
using CorsixNet.Content.Levels;
using CorsixNet.Sim.Economy;

namespace CorsixNet.Client;

/// <summary>What the client was asked to do on the command line.</summary>
internal sealed record ClientOptions
{
    public string? GamePath { get; init; }
    public string? ConfigPath { get; init; }
    public string? InitialLevel { get; init; }
    public string? CampaignPath { get; init; }
    public string? MapPath { get; init; }
    public string? InitialSheet { get; init; }
    public string? ConnectTo { get; init; }
    public int? HostPort { get; init; }
    public bool PublicHost { get; init; }
    public string? PlayerName { get; init; }
    public bool SpriteViewer { get; init; }
    public float Zoom { get; init; } = 1f;
    public MapOverlay Overlay { get; init; } = MapOverlay.None;
    public bool Demo { get; init; }
    public int Upscale { get; init; } = 1;
    public GameSpeed Speed { get; init; } = GameSpeed.Normal;
    public Difficulty Difficulty { get; init; } = Difficulty.Medium;
    public string? CapturePath { get; init; }
    public string? LoadPath { get; init; }
    public bool ContinueGame { get; init; }
}

/// <summary>The client's command line, parsed with System.CommandLine.</summary>
internal static class ClientCommandLine
{
    public const int DefaultPort = 7777;

    private static readonly GameSpeed[] StartSpeeds = [.. Enum.GetValues<GameSpeed>().Where(speed => speed != GameSpeed.SpeedUp)];

    private static readonly Argument<string?> GamePathArgument = new("game folder")
    {
        Description = "The Theme Hospital folder (default: the settings' folder, or wherever the game is found)",
        Arity = ArgumentArity.ZeroOrOne,
        Validators =
        {
            result =>
            {
                // System.CommandLine would take an unknown option for the game folder.
                if (result.Tokens is [{ Value: var text }] && text.StartsWith("--", StringComparison.Ordinal))
                {
                    result.AddError($"Unknown option {text}.");
                }
            },
        },
    };

    private static readonly Option<string?> LevelOption = new("--level")
    {
        Description = "Start on an original level, e.g. LEVEL.L3",
        HelpName = "NAME",
    };

    private static readonly Option<string?> MapOption = new("--map")
    {
        Description = "Show a map file from disk, e.g. a CorsixTH .map or .level file",
        HelpName = "PATH",
    };

    private static readonly Option<string?> CampaignOption = new("--campaign")
    {
        Description = "Put a CorsixTH campaign's levels first, in order (PageDown for the next)",
        HelpName = "PATH",
    };

    private static readonly Option<Difficulty> DifficultyOption = new("--difficulty")
    {
        Description = "Difficulty of the original levels: Easy, Medium or Hard",
        HelpName = "NAME",
        DefaultValueFactory = _ => Difficulty.Medium,
        CustomParser = result =>
        {
            var text = result.Tokens[0].Value;
            if (LevelRulesLoader.TryParseDifficulty(text, out var difficulty))
            {
                return difficulty;
            }

            result.AddError($"Unknown difficulty {text}; use Easy, Medium or Hard.");
            return Difficulty.Medium;
        },
    };

    private static readonly Option<float> ZoomOption = new("--zoom")
    {
        Description = "Initial map zoom, 0.5 to 4",
        HelpName = "FACTOR",
        DefaultValueFactory = _ => 1f,
        CustomParser = result =>
        {
            var text = result.Tokens[0].Value;
            if (float.TryParse(text, CultureInfo.InvariantCulture, out var zoom))
            {
                return zoom;
            }

            result.AddError($"The zoom {text} is not a number.");
            return 1f;
        },
    };

    private static readonly Option<MapOverlay> OverlayOption = new("--overlay")
    {
        Description = $"Initial map overlay: {string.Join(", ", Enum.GetNames<MapOverlay>())}",
        HelpName = "NAME",
        DefaultValueFactory = _ => MapOverlay.None,
    };

    private static readonly Option<bool> DemoOption = new("--demo")
    {
        Description = "Build and furnish a GP's office and a pharmacy and add twenty patients wandering around",
    };

    private static readonly Option<GameSpeed> SpeedOption = new("--speed")
    {
        Description = $"Start a local game at a speed: {string.Join(", ", StartSpeeds)}",
        HelpName = "NAME",
        DefaultValueFactory = _ => GameSpeed.Normal,
        CustomParser = result =>
        {
            var text = result.Tokens[0].Value;
            if (Enum.TryParse<GameSpeed>(text, ignoreCase: true, out var speed) && speed != GameSpeed.SpeedUp)
            {
                return speed;
            }

            result.AddError($"Unknown speed {text}; use {string.Join(", ", StartSpeeds)}.");
            return GameSpeed.Normal;
        },
    };

    private static readonly Option<int> UpscaleOption = new("--upscale")
    {
        Description = "Smooth sprites with the MMPX pixel art filter: 1 (original), 2 or 4 (key U)",
        HelpName = "N",
        DefaultValueFactory = _ => 1,
        CustomParser = result =>
        {
            var text = result.Tokens[0].Value;
            if (int.TryParse(text, CultureInfo.InvariantCulture, out var factor) && factor is 1 or 2 or 4)
            {
                return factor;
            }

            result.AddError($"The upscale factor must be 1, 2 or 4, not {text}.");
            return 1;
        },
    };

    private static readonly Option<string?> LoadOption = new("--load")
    {
        Description = "Load a saved game, e.g. the quick save or an autosave",
        HelpName = "FILE",
    };

    private static readonly Option<bool> ContinueOption = new("--continue")
    {
        Description = "Load the newest autosave",
    };

    private static readonly Option<string?> CaptureOption = new("--capture")
    {
        Description = "After a few seconds, save the map view to a PNG file and quit",
        HelpName = "FILE",
    };

    private static readonly Option<bool> SpriteViewerOption = new("--sprite-viewer")
    {
        Description = "Browse sprite sheets instead of maps",
    };

    private static readonly Option<string?> SheetOption = new("--sheet")
    {
        Description = "Open the sprite viewer on a sheet, e.g. DATA/VBLK-0",
        HelpName = "NAME",
    };

    private static readonly Option<string?> ConfigOption = new("--config")
    {
        Description = "Use another settings file (default: config.txt in the CorsixNet settings folder)",
        HelpName = "FILE",
    };

    private static readonly Option<int> HostOption = new("--host")
    {
        Description = $"Multiplayer: host a game on the level (default LEVEL.L20, a two-player map) and join it, on PORT (default {DefaultPort})",
        HelpName = "PORT",
        Arity = ArgumentArity.ZeroOrOne,
        CustomParser = result =>
        {
            if (result.Tokens.Count == 0)
            {
                return DefaultPort;
            }

            var text = result.Tokens[0].Value;
            if (int.TryParse(text, CultureInfo.InvariantCulture, out var port))
            {
                return port;
            }

            result.AddError($"The port {text} is not a number.");
            return DefaultPort;
        },
    };

    private static readonly Option<bool> PublicOption = new("--public")
    {
        Description = "Multiplayer: with --host, accept connections from other machines, not only this one",
    };

    private static readonly Option<string?> ConnectOption = new("--connect")
    {
        Description = $"Multiplayer: join a game at HOST or HOST:PORT (default port {DefaultPort})",
        HelpName = "ADDRESS",
    };

    private static readonly Option<string?> NameOption = new("--name")
    {
        Description = "Multiplayer: player name shown to the server",
        HelpName = "NAME",
    };

    /// <summary>Builds the root command. A new one each time, since a parse result keeps hold of it.</summary>
    public static RootCommand CreateCommand() => new("CorsixNet, a reimplementation of Theme Hospital")
    {
        GamePathArgument,
        LevelOption,
        MapOption,
        CampaignOption,
        DifficultyOption,
        ZoomOption,
        OverlayOption,
        DemoOption,
        SpeedOption,
        UpscaleOption,
        LoadOption,
        ContinueOption,
        CaptureOption,
        SpriteViewerOption,
        SheetOption,
        ConfigOption,
        HostOption,
        PublicOption,
        ConnectOption,
        NameOption,
    };

    public static ParseResult Parse(IReadOnlyList<string> args) => CreateCommand().Parse(WithHostPort(args));

    /// <summary>
    /// The port after --host is optional, and a word after it that is not a number is the game folder, as before.
    /// System.CommandLine would take that word as the port, so the default port is put in front of it.
    /// </summary>
    private static List<string> WithHostPort(IReadOnlyList<string> args)
    {
        var result = new List<string>(args.Count + 1);
        for (var i = 0; i < args.Count; i++)
        {
            result.Add(args[i]);
            if (args[i] == HostOption.Name
                && i + 1 < args.Count
                && !args[i + 1].StartsWith('-')
                && !int.TryParse(args[i + 1], CultureInfo.InvariantCulture, out _))
            {
                result.Add(DefaultPort.ToString(CultureInfo.InvariantCulture));
            }
        }

        return result;
    }

    /// <summary>The options of a parse without errors.</summary>
    public static ClientOptions Bind(ParseResult result)
    {
        var sheet = result.GetValue(SheetOption);
        return new ClientOptions
        {
            GamePath = result.GetValue(GamePathArgument),
            ConfigPath = FullPath(result.GetValue(ConfigOption)),
            InitialLevel = result.GetValue(LevelOption),
            CampaignPath = result.GetValue(CampaignOption),
            MapPath = result.GetValue(MapOption),
            InitialSheet = sheet,
            ConnectTo = result.GetValue(ConnectOption),
            HostPort = result.GetResult(HostOption) is null ? null : result.GetValue(HostOption),
            PublicHost = result.GetValue(PublicOption),
            PlayerName = result.GetValue(NameOption),
            SpriteViewer = result.GetValue(SpriteViewerOption) || sheet is not null,
            Zoom = result.GetValue(ZoomOption),
            Overlay = result.GetValue(OverlayOption),
            Demo = result.GetValue(DemoOption),
            Upscale = result.GetValue(UpscaleOption),
            Speed = result.GetValue(SpeedOption),
            Difficulty = result.GetValue(DifficultyOption),
            CapturePath = FullPath(result.GetValue(CaptureOption)),
            LoadPath = FullPath(result.GetValue(LoadOption)),
            ContinueGame = result.GetValue(ContinueOption),
        };
    }

    private static string? FullPath(string? path) => path is null ? null : Path.GetFullPath(path);
}
