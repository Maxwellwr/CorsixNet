using CorsixNet.Client;
using CorsixNet.Formats;

var install = args.Length > 0
    ? (ThemeHospitalInstall.TryOpen(args[0], out var explicitInstall, out var error) ? explicitInstall : Fail(error))
    : ThemeHospitalInstall.Locate();

if (install is null)
{
    Console.Error.WriteLine(
        $"Theme Hospital data was not found. Pass the game folder as the first argument or set {ThemeHospitalInstall.PathEnvironmentVariable}.");
    return 1;
}

Console.WriteLine($"Using Theme Hospital data from {install.RootPath}{(install.IsDemo ? " (demo)" : "")}");

using var game = new CorsixNetGame(install);
game.Run();
return 0;

static ThemeHospitalInstall? Fail(string message)
{
    Console.Error.WriteLine(message);
    return null;
}
