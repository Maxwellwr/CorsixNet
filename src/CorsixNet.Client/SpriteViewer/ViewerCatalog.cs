using CorsixNet.Formats;

namespace CorsixNet.Client.SpriteViewer;

/// <summary>A sprite sheet or full-screen bitmap that the viewer can show.</summary>
/// <param name="Folder">Game folder, e.g. DATA or QDATA.</param>
/// <param name="Name">File name without extension.</param>
/// <param name="IsBitmap">True for a raw 640x480 bitmap, false for a TAB/DAT sprite sheet.</param>
/// <param name="Complex">Whether a sprite sheet uses the complex chunk encoding.</param>
/// <param name="PaletteFolder">Folder of the default palette.</param>
/// <param name="PaletteFile">File name of the default palette.</param>
internal sealed record ViewerItem(
    string Folder,
    string Name,
    bool IsBitmap,
    bool Complex,
    string PaletteFolder,
    string PaletteFile)
{
    public string Title => $"{Folder}/{Name}";
}

/// <summary>Finds everything in the game data that the viewer can show.</summary>
internal static class ViewerCatalog
{
    private const string DefaultPaletteFolder = "DATA";
    private const string DefaultPaletteFile = "MPALETTE.DAT";

    /// <summary>Sheets that use the simple chunk encoding; see CorsixTH's graphics.lua.</summary>
    private static readonly HashSet<string> SimpleSheets = new(StringComparer.OrdinalIgnoreCase)
    {
        "VBLK-0", "VSPR-0", "MPOINTER", "MONEY01V", "SPOINTER",
    };

    public static IReadOnlyList<ViewerItem> Build(ThemeHospitalInstall install)
    {
        var items = new List<ViewerItem>();
        foreach (var folder in new[] { "DATA", "QDATA" })
        {
            items.AddRange(FindSheets(install, folder));
        }

        items.AddRange(FindBitmaps(install, "QDATA"));
        return items;
    }

    /// <summary>All palettes in the game, as (folder, file) pairs.</summary>
    public static IReadOnlyList<(string Folder, string File)> FindPalettes(ThemeHospitalInstall install)
    {
        var palettes = new List<(string, string)> { (DefaultPaletteFolder, DefaultPaletteFile) };
        palettes.AddRange(FilesWithExtension(install, "QDATA", ".PAL").Select(name => ("QDATA", name + ".PAL")));
        return palettes;
    }

    private static IEnumerable<ViewerItem> FindSheets(ThemeHospitalInstall install, string folder)
    {
        foreach (var name in FilesWithExtension(install, folder, ".TAB"))
        {
            if (!install.FileExists(folder, name + ".DAT"))
            {
                continue;
            }

            var simple = SimpleSheets.Contains(name) || name.StartsWith("FONT", StringComparison.OrdinalIgnoreCase);
            var (paletteFolder, paletteFile) = GuessPalette(install, folder, name);
            yield return new ViewerItem(folder, name, IsBitmap: false, Complex: !simple, paletteFolder, paletteFile);
        }
    }

    private static IEnumerable<ViewerItem> FindBitmaps(ThemeHospitalInstall install, string folder)
    {
        // Full-screen bitmaps come with a palette of the same name.
        foreach (var name in FilesWithExtension(install, folder, ".PAL"))
        {
            if (install.FileExists(folder, name + ".DAT"))
            {
                yield return new ViewerItem(folder, name, IsBitmap: true, Complex: false, folder, name + ".PAL");
            }
        }
    }

    /// <summary>UI sheets named like BANK02V use the palette of their background, BANK01V.PAL.</summary>
    private static (string Folder, string File) GuessPalette(ThemeHospitalInstall install, string folder, string name)
    {
        if (name.Length > 3 && name.EndsWith('V') && char.IsAsciiDigit(name[^2]) && char.IsAsciiDigit(name[^3]))
        {
            var candidate = $"{name[..^3]}01V.PAL";
            if (!name.StartsWith("FONT", StringComparison.OrdinalIgnoreCase) && install.FileExists(folder, candidate))
            {
                return (folder, candidate);
            }
        }

        return (DefaultPaletteFolder, DefaultPaletteFile);
    }

    private static IEnumerable<string> FilesWithExtension(ThemeHospitalInstall install, string folder, string extension)
    {
        var directory = install.FindDirectory(folder);
        if (directory is null)
        {
            return [];
        }

        return Directory.EnumerateFiles(directory)
            .Where(path => Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFileNameWithoutExtension(path).ToUpperInvariant())
            .Order(StringComparer.Ordinal);
    }
}
