using System.Globalization;
using System.Net;
using CorsixNet.Client;
using CorsixNet.Client.Maps;
using CorsixNet.Client.Sessions;
using CorsixNet.Client.SpriteViewer;
using CorsixNet.Content;
using CorsixNet.Content.Interface;
using CorsixNet.Content.Levels;
using CorsixNet.Content.Saves;
using CorsixNet.Content.Settings;
using CorsixNet.Formats;
using CorsixNet.Net.Lockstep;
using CorsixNet.Net.Transport;
using CorsixNet.Sim.Economy;

var parseResult = ClientCommandLine.Parse(args);
if (parseResult.Action is not null)
{
    // --help, --version or a mistake on the command line: System.CommandLine prints it.
    return parseResult.Invoke();
}

var options = ClientCommandLine.Bind(parseResult);
var configPath = options.ConfigPath;
var initialLevel = options.InitialLevel;
var playerName = options.PlayerName;
var loadPath = options.LoadPath;

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
if (options.GamePath is null)
{
    // The settings' game folder if it is there, as CorsixTH's theme_hospital_install; otherwise look for the game.
    install = config.ThemeHospitalInstall != GameConfig.PlaceholderInstall
        && ThemeHospitalInstall.TryOpen(config.ThemeHospitalInstall, out var configuredInstall, out _)
        ? configuredInstall
        : ThemeHospitalInstall.Locate();
}
else if (!ThemeHospitalInstall.TryOpen(options.GamePath, out install, out var error))
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
if (options.SpriteViewer)
{
    createScreen = device => new SpriteViewerScreen(install, device, options.InitialSheet);
}
else
{
    var levels = MapViewScreen.OriginalLevels(install, options.Difficulty).ToList();
    if (options.HostPort is not null && initialLevel is null && options.MapPath is null)
    {
        // A two-player map, so that someone can join.
        initialLevel = levels.Any(level => level.Name == "LEVEL.L20") ? "LEVEL.L20" : null;
    }

    if (options.CampaignPath is not null)
    {
        var (_, levelPaths) = LevelMapLoader.LoadCampaign(Path.GetFullPath(options.CampaignPath));
        levels.InsertRange(0, levelPaths.Select(path => new LevelSource(Path.GetFileName(path), () => LevelMapLoader.LoadFile(path, install))));
    }

    if (options.MapPath is not null)
    {
        var fullPath = Path.GetFullPath(options.MapPath);
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
        if (options.HostPort is { } port)
        {
            if (levels.Count == 0)
            {
                Console.Error.WriteLine("No level to host.");
                return 1;
            }

            var level = levels[0];
            var server = new LockstepServer(level.Load().StartSimulation(seed: 1)) { Log = Console.WriteLine };
            var listener = new TcpConnectionListener(options.PublicHost ? IPAddress.Any : IPAddress.Loopback, port);
            var runner = new ServerRunner(server, listener, LocalSession.TickInterval);
            Console.WriteLine($"Hosting {level.Name} on port {runner.Port}{(options.PublicHost ? " for all network interfaces" : " (this machine only; add --public for others)")}.");
            var connection = TcpConnection.ConnectAsync("127.0.0.1", runner.Port).GetAwaiter().GetResult();
            networkSession = new NetworkSession(level.Name, new LockstepClient(connection, playerName, BaseGameContent.Create()), runner);
        }
        else if (options.ConnectTo is not null)
        {
            var (host, remotePort) = ParseAddress(options.ConnectTo, ClientCommandLine.DefaultPort);
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
        if (options.ContinueGame)
        {
            loadPath = new SaveGameStore(SaveGameStore.DefaultDirectory()).MostRecentAutosave();
        }

        var screen = new MapViewScreen(install, device, levels, options.Zoom, options.Overlay, options.Demo, networkSession, options.Upscale, options.Speed, loadPath, config);
        if (options.CapturePath is not null)
        {
            // Give the demo time to build and furnish its room before capturing.
            _ = Task.Delay(TimeSpan.FromSeconds(6)).ContinueWith(_ => screen.RequestCapture(options.CapturePath), TaskScheduler.Default);
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
