using System.Globalization;
using System.Net;
using CorsixNet.Content.Levels;
using CorsixNet.Formats;
using CorsixNet.Net.Lockstep;
using CorsixNet.Net.Transport;

const string Usage = """
    Usage: CorsixNet.Server [game folder] [options]
      --level NAME   Original level to play (default LEVEL.L20, a two-player map)
      --map PATH     Map file to play instead, e.g. a CorsixTH .map file
      --port PORT    TCP port (default 7777)
      --public       Accept connections from other machines, not only this one
      --seed N       Seed for the simulation's random numbers (default 1)
    """;

// Same as the client's "Normal" speed: CorsixTH runs a tick every 3 frames of 18 ms.
var tickInterval = TimeSpan.FromMilliseconds(54);

string? gamePath = null;
var level = "LEVEL.L20";
string? mapPath = null;
var port = 7777;
var publicHost = false;
ulong seed = 1;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--level" when i + 1 < args.Length:
            level = args[++i];
            break;
        case "--map" when i + 1 < args.Length:
            mapPath = args[++i];
            break;
        case "--port" when i + 1 < args.Length && int.TryParse(args[i + 1], CultureInfo.InvariantCulture, out var p):
            port = p;
            i++;
            break;
        case "--seed" when i + 1 < args.Length && ulong.TryParse(args[i + 1], CultureInfo.InvariantCulture, out var s):
            seed = s;
            i++;
            break;
        case "--public":
            publicHost = true;
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

LoadedLevelMap loaded;
string levelName;
try
{
    if (mapPath is not null)
    {
        // The game's level files give the money settings, if the game is at hand.
        var settings = gamePath is null ? ThemeHospitalInstall.Locate()
            : ThemeHospitalInstall.TryOpen(gamePath, out var found, out _) ? found : null;
        loaded = LevelMapLoader.LoadFile(mapPath, settings);
        levelName = Path.GetFileName(mapPath);
    }
    else
    {
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
            Console.Error.WriteLine($"Theme Hospital data was not found. Pass the game folder or set {ThemeHospitalInstall.PathEnvironmentVariable}.");
            return 1;
        }

        loaded = LevelMapLoader.LoadOriginal(install, level);
        levelName = level;
    }
}
catch (Exception e) when (e is IOException or InvalidDataException)
{
    Console.Error.WriteLine($"Cannot load the level: {e.Message}");
    return 1;
}

var server = new LockstepServer(loaded.StartSimulation(seed)) { Log = line => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {line}") };
using var runner = new ServerRunner(server, new TcpConnectionListener(publicHost ? IPAddress.Any : IPAddress.Loopback, port), tickInterval);
Console.WriteLine($"Serving {levelName} ({loaded.Map.PlayerCount} players) on port {runner.Port}{(publicHost ? "" : " (this machine only; add --public for others)")}. Press Ctrl+C to stop.");

using var stop = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Set();
};
stop.Wait();
Console.WriteLine("Stopping.");
return 0;
