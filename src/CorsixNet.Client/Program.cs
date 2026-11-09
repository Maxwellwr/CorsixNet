using System.Globalization;
using System.Net;
using CorsixNet.Client;
using CorsixNet.Client.Maps;
using CorsixNet.Client.Sessions;
using CorsixNet.Client.SpriteViewer;
using CorsixNet.Content;
using CorsixNet.Content.Levels;
using CorsixNet.Content.Saves;
using CorsixNet.Content.Settings;
using CorsixNet.Formats;
using CorsixNet.Net.Lockstep;
using CorsixNet.Net.Transport;
using CorsixNet.Sim.Economy;

const int DefaultPort = 7777;
const string Usage = """
    Usage: CorsixNet [game folder] [options]
      --level NAME      Start on an original level, e.g. LEVEL.L3
      --map PATH        Show a map file from disk, e.g. a CorsixTH .map or .level file
      --campaign PATH   Put a CorsixTH campaign's levels first, in order (PageDown for the next)
      --difficulty NAME Difficulty of the original levels: Easy, Medium (default) or Hard
      --zoom FACTOR     Initial map zoom, 0.5 to 4
      --overlay NAME    Initial map overlay: None, Passable, Hospital or Parcels
      --demo            Build and furnish a GP's office and a pharmacy and add twenty patients wandering around
      --speed NAME      Start a local game at a speed: Pause, Slowest, Slower, Normal, MaxSpeed or AndThenSomeMore
      --upscale N       Smooth sprites with the MMPX pixel art filter: 1 (original), 2 or 4 (key U)
      --load FILE       Load a saved game, e.g. the quick save or an autosave
      --continue        Load the newest autosave
      --capture FILE    After a few seconds, save the map view to a PNG file and quit
      --sprite-viewer   Browse sprite sheets instead of maps
      --sheet NAME      Open the sprite viewer on a sheet, e.g. DATA/VBLK-0
      --config FILE     Use another settings file (default: config.txt in the CorsixNet settings folder)

    Multiplayer (lockstep):
      --host [PORT]     Host a game on the level (default LEVEL.L20, a two-player map) and join it
      --public          With --host, accept connections from other machines, not only this one
      --connect ADDRESS Join a game at HOST or HOST:PORT (default port 7777)
      --name NAME       Player name shown to the server
    """;

string? gamePath = null;
string? configPath = null;
string? initialLevel = null;
string? campaignPath = null;
string? mapPath = null;
string? initialSheet = null;
string? connectTo = null;
int? hostPort = null;
var publicHost = false;
string? playerName = null;
var spriteViewer = false;
var zoom = 1f;
var overlay = MapOverlay.None;
var demo = false;
var upscale = 1;
var speed = GameSpeed.Normal;
var difficulty = Difficulty.Medium;
string? capturePath = null;
string? loadPath = null;
var continueGame = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--level" when i + 1 < args.Length:
            initialLevel = args[++i];
            break;
        case "--map" when i + 1 < args.Length:
            mapPath = args[++i];
            break;
        case "--campaign" when i + 1 < args.Length:
            campaignPath = args[++i];
            break;
        case "--difficulty" when i + 1 < args.Length && LevelRulesLoader.TryParseDifficulty(args[i + 1], out var d):
            difficulty = d;
            i++;
            break;
        case "--zoom" when i + 1 < args.Length && float.TryParse(args[i + 1], CultureInfo.InvariantCulture, out var z):
            zoom = z;
            i++;
            break;
        case "--overlay" when i + 1 < args.Length && Enum.TryParse<MapOverlay>(args[i + 1], ignoreCase: true, out var o):
            overlay = o;
            i++;
            break;
        case "--demo":
            demo = true;
            break;
        case "--upscale" when i + 1 < args.Length && int.TryParse(args[i + 1], CultureInfo.InvariantCulture, out var u) && u is 1 or 2 or 4:
            upscale = u;
            i++;
            break;
        case "--speed" when i + 1 < args.Length && Enum.TryParse<GameSpeed>(args[i + 1], ignoreCase: true, out var s) && s != GameSpeed.SpeedUp:
            speed = s;
            i++;
            break;
        case "--host":
            if (i + 1 < args.Length && int.TryParse(args[i + 1], CultureInfo.InvariantCulture, out var p))
            {
                hostPort = p;
                i++;
            }
            else
            {
                hostPort = DefaultPort;
            }

            break;
        case "--public":
            publicHost = true;
            break;
        case "--connect" when i + 1 < args.Length:
            connectTo = args[++i];
            break;
        case "--name" when i + 1 < args.Length:
            playerName = args[++i];
            break;
        case "--sheet" when i + 1 < args.Length:
            initialSheet = args[++i];
            spriteViewer = true;
            break;
        case "--load" when i + 1 < args.Length:
            loadPath = Path.GetFullPath(args[++i]);
            break;
        case "--continue":
            continueGame = true;
            break;
        case "--capture" when i + 1 < args.Length:
            capturePath = Path.GetFullPath(args[++i]);
            break;
        case "--config" when i + 1 < args.Length:
            configPath = Path.GetFullPath(args[++i]);
            break;
        case "--sprite-viewer":
            spriteViewer = true;
            break;
        case "--help" or "-h":
            Console.WriteLine(Usage);
            return 0;
        default:
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"Unknown option {args[i]}.\n{Usage}");
                return 1;
            }

            gamePath = args[i];
            break;
    }
}

GameConfig config;
try
{
    configPath ??= GameConfig.DefaultPath();
    config = GameConfig.Load(configPath);
}
catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"The settings file {configPath} could not be read ({e.Message}); using the defaults.");
    config = new GameConfig();
}

playerName ??= config.PlayerName is { Length: > 0 } configured ? configured : "Player";

ThemeHospitalInstall? install;
if (gamePath is null)
{
    // The settings' game folder if it is there, as CorsixTH's theme_hospital_install; otherwise look for the game.
    install = config.ThemeHospitalInstall != GameConfig.PlaceholderInstall
        && ThemeHospitalInstall.TryOpen(config.ThemeHospitalInstall, out var configuredInstall, out _)
        ? configuredInstall
        : ThemeHospitalInstall.Locate();
}
else if (!ThemeHospitalInstall.TryOpen(gamePath, out install, out var error))
{
    Console.Error.WriteLine(error);
    return 1;
}

if (install is null)
{
    Console.Error.WriteLine(
        $"Theme Hospital data was not found. Pass the game folder as an argument or set {ThemeHospitalInstall.PathEnvironmentVariable}.");
    return 1;
}

Console.WriteLine($"Using Theme Hospital data from {install.RootPath}{(install.IsDemo ? " (demo)" : "")}");

Func<Microsoft.Xna.Framework.Graphics.GraphicsDevice, IScreen> createScreen;
if (spriteViewer)
{
    createScreen = device => new SpriteViewerScreen(install, device, initialSheet);
}
else
{
    var levels = MapViewScreen.OriginalLevels(install, difficulty).ToList();
    if (hostPort is not null && initialLevel is null && mapPath is null)
    {
        // A two-player map, so that someone can join.
        initialLevel = levels.Any(level => level.Name == "LEVEL.L20") ? "LEVEL.L20" : null;
    }

    if (campaignPath is not null)
    {
        var (_, levelPaths) = LevelMapLoader.LoadCampaign(Path.GetFullPath(campaignPath));
        levels.InsertRange(0, levelPaths.Select(path => new LevelSource(Path.GetFileName(path), () => LevelMapLoader.LoadFile(path, install))));
    }

    if (mapPath is not null)
    {
        var fullPath = Path.GetFullPath(mapPath);
        levels.Insert(0, new LevelSource(Path.GetFileName(fullPath), () => config.FreeBuildMode
            ? LevelMapLoader.LoadFile(fullPath, install).WithFreeBuild()
            : LevelMapLoader.LoadFile(fullPath, install)));
    }
    else if (initialLevel is not null)
    {
        var index = levels.FindIndex(level => string.Equals(level.Name, initialLevel, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            Console.Error.WriteLine($"Level {initialLevel} was not found in {install.RootPath}.");
            return 1;
        }

        levels = [.. levels[index..], .. levels[..index]];
    }

    IGameSession? networkSession = null;
    try
    {
        if (hostPort is { } port)
        {
            if (levels.Count == 0)
            {
                Console.Error.WriteLine("No level to host.");
                return 1;
            }

            var level = levels[0];
            var server = new LockstepServer(level.Load().StartSimulation(seed: 1)) { Log = Console.WriteLine };
            var listener = new TcpConnectionListener(publicHost ? IPAddress.Any : IPAddress.Loopback, port);
            var runner = new ServerRunner(server, listener, LocalSession.TickInterval);
            Console.WriteLine($"Hosting {level.Name} on port {runner.Port}{(publicHost ? " for all network interfaces" : " (this machine only; add --public for others)")}.");
            var connection = TcpConnection.ConnectAsync("127.0.0.1", runner.Port).GetAwaiter().GetResult();
            networkSession = new NetworkSession(level.Name, new LockstepClient(connection, playerName, BaseGameContent.Create()), runner);
        }
        else if (connectTo is not null)
        {
            var (host, remotePort) = ParseAddress(connectTo, DefaultPort);
            Console.WriteLine($"Connecting to {host}:{remotePort}...");
            var connection = TcpConnection.ConnectAsync(host, remotePort).GetAwaiter().GetResult();
            networkSession = new NetworkSession($"{host}:{remotePort}", new LockstepClient(connection, playerName, BaseGameContent.Create()));
        }
    }
    catch (System.Net.Sockets.SocketException e)
    {
        Console.Error.WriteLine($"Network error: {e.Message}");
        return 1;
    }

    createScreen = device =>
    {
        if (continueGame)
        {
            loadPath = new SaveGameStore(SaveGameStore.DefaultDirectory()).MostRecentAutosave();
        }

        var screen = new MapViewScreen(install, device, levels, zoom, overlay, demo, networkSession, upscale, speed, loadPath, config);
        if (capturePath is not null)
        {
            // Give the demo time to build and furnish its room before capturing.
            _ = Task.Delay(TimeSpan.FromSeconds(6)).ContinueWith(_ => screen.RequestCapture(capturePath), TaskScheduler.Default);
        }

        return screen;
    };
}

using var game = new CorsixNetGame($"CorsixNet - {install.RootPath}", createScreen, config.Width, config.Height, config.Fullscreen);
game.Run();
return 0;

static (string Host, int Port) ParseAddress(string address, int defaultPort)
{
    var colon = address.LastIndexOf(':');
    if (colon > 0 && int.TryParse(address.AsSpan(colon + 1), CultureInfo.InvariantCulture, out var port))
    {
        return (address[..colon], port);
    }

    return (address, defaultPort);
}
