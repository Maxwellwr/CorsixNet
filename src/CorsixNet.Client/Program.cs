using CorsixNet.Client;
using CorsixNet.Formats;

// Usage: CorsixNet [game folder] [--sheet FOLDER/NAME]
string? gamePath = null;
string? initialSheet = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--sheet" && i + 1 < args.Length)
    {
        initialSheet = args[++i];
    }
    else
    {
        gamePath = args[i];
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

using var game = new CorsixNetGame(install, initialSheet);
game.Run();
return 0;
