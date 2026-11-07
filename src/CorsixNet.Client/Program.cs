using CorsixNet.Client;
using CorsixNet.Client.Maps;
using CorsixNet.Client.SpriteViewer;
using CorsixNet.Content.Levels;
using CorsixNet.Formats;

const string Usage = """
    Usage: CorsixNet [game folder] [options]
      --level NAME      Start on an original level, e.g. LEVEL.L3
      --map PATH        Show a map file from disk, e.g. a CorsixTH .map file
      --zoom FACTOR     Initial map zoom, 0.5 to 4
      --overlay NAME    Initial map overlay: None, Passable, Hospital or Parcels
      --sprite-viewer   Browse sprite sheets instead of maps
      --sheet NAME      Open the sprite viewer on a sheet, e.g. DATA/VBLK-0
    """;

string? gamePath = null;
string? initialLevel = null;
string? mapPath = null;
string? initialSheet = null;
var spriteViewer = false;
var zoom = 1f;
var overlay = MapOverlay.None;
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
        case "--zoom" when i + 1 < args.Length && float.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out var z):
            zoom = z;
            i++;
            break;
        case "--overlay" when i + 1 < args.Length && Enum.TryParse<MapOverlay>(args[i + 1], ignoreCase: true, out var o):
            overlay = o;
            i++;
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

    createScreen = device => new MapViewScreen(install, device, levels, zoom, overlay);
}

using var game = new CorsixNetGame($"CorsixNet - {install.RootPath}", createScreen);
game.Run();
return 0;
