using System.Globalization;
using System.Net;
using CorsixNet.Client;
using CorsixNet.Client.Maps;
using CorsixNet.Client.Sessions;
using CorsixNet.Client.SpriteViewer;
using CorsixNet.Content;
using CorsixNet.Content.Levels;
using CorsixNet.Formats;
using CorsixNet.Net.Lockstep;
using CorsixNet.Net.Transport;

const int DefaultPort = 7777;
const string Usage = """
    Usage: CorsixNet [game folder] [options]
      --level NAME      Start on an original level, e.g. LEVEL.L3
      --map PATH        Show a map file from disk, e.g. a CorsixTH .map file
      --zoom FACTOR     Initial map zoom, 0.5 to 4
      --overlay NAME    Initial map overlay: None, Passable, Hospital or Parcels
      --demo            Build a GP's office and add twenty patients wandering around the hospital
      --sprite-viewer   Browse sprite sheets instead of maps
      --sheet NAME      Open the sprite viewer on a sheet, e.g. DATA/VBLK-0

    Multiplayer (lockstep):
      --host [PORT]     Host a game on the level (default LEVEL.L20, a two-player map) and join it
      --public          With --host, accept connections from other machines, not only this one
      --connect ADDRESS Join a game at HOST or HOST:PORT (default port 7777)
      --name NAME       Player name shown to the server
    """;

string? gamePath = null;
string? initialLevel = null;
string? mapPath = null;
string? initialSheet = null;
string? connectTo = null;
int? hostPort = null;
var publicHost = false;
var playerName = "Player";
var spriteViewer = false;
var zoom = 1f;
var overlay = MapOverlay.None;
var demo = false;
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

ThemeHospitalInstall? install;
if (gamePath is null)
{
    install = ThemeHospitalInstall.Locate();
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
    var levels = MapViewScreen.OriginalLevels(install).ToList();
    if (hostPort is not null && initialLevel is null && mapPath is null)
    {
        // A two-player map, so that someone can join.
        initialLevel = levels.Any(level => level.Name == "LEVEL.L20") ? "LEVEL.L20" : null;
    }

    if (mapPath is not null)
    {
        var fullPath = Path.GetFullPath(mapPath);
        levels.Insert(0, new LevelSource(Path.GetFileName(fullPath), () => LevelMapLoader.LoadFile(fullPath)));
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

    createScreen = device => new MapViewScreen(install, device, levels, zoom, overlay, demo, networkSession);
}

using var game = new CorsixNetGame($"CorsixNet - {install.RootPath}", createScreen);
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
