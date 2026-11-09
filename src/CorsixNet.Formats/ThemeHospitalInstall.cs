using System.Diagnostics.CodeAnalysis;

namespace CorsixNet.Formats;

/// <summary>
/// A folder containing the data files of the original Theme Hospital.
/// </summary>
/// <remarks>
/// File names are resolved case-insensitively on every platform, because copies
/// of the game differ in casing (DOS CDs use upper case, some repacks lower case).
/// </remarks>
public sealed class ThemeHospitalInstall
{
    /// <summary>Environment variable that overrides automatic detection.</summary>
    public const string PathEnvironmentVariable = "CORSIXNET_TH_PATH";

    /// <summary>Files present in every full and demo copy of the game.</summary>
    private static readonly string[][] RequiredFiles =
    [
        ["DATA", "VBLK-0.TAB"],
        ["LEVELS", "LEVEL.L1"],
        ["QDATA", "SPOINTER.DAT"],
    ];

    /// <summary>Folders below the given path that may hold the game, e.g. the EA/Origin layout.</summary>
    private static readonly string[][] NestedRoots =
    [
        [],
        ["data", "Game"],
    ];

    private static readonly EnumerationOptions CaseInsensitive = new()
    {
        MatchCasing = MatchCasing.CaseInsensitive,
        MatchType = MatchType.Simple,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.None,
    };

    private ThemeHospitalInstall(string rootPath, bool isDemo)
    {
        RootPath = rootPath;
        IsDemo = isDemo;
    }

    /// <summary>Full path of the folder that contains DATA, LEVELS, QDATA and the other game folders.</summary>
    public string RootPath { get; }

    /// <summary>Whether the files come from the demo version of the game.</summary>
    public bool IsDemo { get; }

    /// <summary>
    /// Opens the game at <paramref name="path"/>, which may be the game folder itself
    /// or an installation folder that contains it (such as the EA <c>data\Game</c> layout).
    /// </summary>
    public static bool TryOpen(
        string path,
        [NotNullWhen(true)] out ThemeHospitalInstall? install,
        [NotNullWhen(false)] out string? error)
    {
        install = null;
        if (!Directory.Exists(path))
        {
            error = $"Folder '{path}' does not exist.";
            return false;
        }

        string[]? firstMissing = null;
        foreach (var nested in NestedRoots)
        {
            if (ResolvePath(path, nested) is not { } root || !Directory.Exists(root))
            {
                continue;
            }

            var missing = RequiredFiles
                .Where(file => ResolvePath(root, file) is null)
                .Select(file => string.Join('/', file))
                .ToArray();
            if (missing.Length == 0)
            {
                install = new ThemeHospitalInstall(Path.GetFullPath(root), ResolvePath(root, ["DATAM", "DEMO.DAT"]) is not null);
                error = null;
                return true;
            }

            firstMissing ??= missing;
        }

        error = $"Folder '{path}' does not contain the Theme Hospital data files (missing: {string.Join(", ", firstMissing ?? RequiredFiles.Select(f => string.Join('/', f)))}).";
        return false;
    }

    /// <summary>
    /// Finds the game using <see cref="PathEnvironmentVariable"/> or, failing that,
    /// the usual installation folders. Returns null if no copy is found.
    /// </summary>
    public static ThemeHospitalInstall? Locate()
    {
        var overridePath = Environment.GetEnvironmentVariable(PathEnvironmentVariable);
        if (!string.IsNullOrEmpty(overridePath))
        {
            return TryOpen(overridePath, out var configured, out _) ? configured : null;
        }

        foreach (var candidate in DefaultSearchPaths())
        {
            if (TryOpen(candidate, out var install, out _))
            {
                return install;
            }
        }

        return null;
    }

    /// <summary>Folders where copies of the game are commonly installed, most likely first.</summary>
    public static IEnumerable<string> DefaultSearchPaths()
    {
        string[] folderNames =
        [
            "Theme Hospital",
            "ThemeHospital",
            "TH97",
            "HOSP",
            Path.Combine("Games", "Theme Hospital"),
            Path.Combine("GOG Games", "Theme Hospital"),
            Path.Combine("GOG Galaxy", "Games", "Theme Hospital"),
            Path.Combine("GOG.com", "Theme Hospital"),
            Path.Combine("EA Games", "Theme Hospital"),
            Path.Combine("Origin Games", "Theme Hospital"),
        ];

        var bases = new List<string>();
        AddIfSet(bases, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        AddIfSet(bases, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        AddIfSet(bases, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddIfSet(bases, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        if (OperatingSystem.IsWindows())
        {
            bases.AddRange(DriveInfo.GetDrives()
                .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
                .Select(drive => drive.RootDirectory.FullName));
        }

        foreach (var basePath in bases.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var folder in folderNames)
            {
                yield return Path.Combine(basePath, folder);
            }
        }

        if (OperatingSystem.IsMacOS())
        {
            // GOG's macOS bundle wraps the DOS game in a Boxer disk image folder.
            yield return "/Applications/Theme Hospital.app/Contents/Resources/game/Theme Hospital.app/Contents/Resources/Theme Hospital.boxer/C.harddisk";
        }
    }

    /// <summary>Returns the actual full path of a file below <see cref="RootPath"/>, or null if it does not exist.</summary>
    public string? FindFile(params string[] segments)
    {
        var path = ResolvePath(RootPath, segments);
        return path is not null && File.Exists(path) ? path : null;
    }

    /// <summary>Returns the actual full path of a folder below <see cref="RootPath"/>, or null if it does not exist.</summary>
    public string? FindDirectory(params string[] segments)
    {
        var path = ResolvePath(RootPath, segments);
        return path is not null && Directory.Exists(path) ? path : null;
    }

    /// <summary>Whether a file exists below <see cref="RootPath"/>.</summary>
    public bool FileExists(params string[] segments) => FindFile(segments) is not null;

    /// <summary>Opens a file below <see cref="RootPath"/> for reading.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public Stream OpenRead(params string[] segments)
    {
        return File.OpenRead(FindFile(segments) ?? throw NotFound(segments));
    }

    /// <summary>Reads a whole file below <see cref="RootPath"/>.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public byte[] ReadAllBytes(params string[] segments)
    {
        return File.ReadAllBytes(FindFile(segments) ?? throw NotFound(segments));
    }

    /// <summary>Reads a whole file, decompressing it if it is RNC compressed.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is RNC compressed but corrupt.</exception>
    public byte[] ReadDataFile(params string[] segments)
    {
        return Rnc.DecompressIfNeeded(ReadAllBytes(segments));
    }

    /// <summary>Loads a palette file such as <c>DATA/MPALETTE.DAT</c> or <c>QDATA/BANK01V.PAL</c>.</summary>
    public Palette LoadPalette(string folder, string fileName)
    {
        return Palette.Parse(ReadDataFile(folder, fileName));
    }

    /// <summary>Loads the sprite sheet <c>{name}.TAB</c> / <c>{name}.DAT</c> from <paramref name="folder"/>.</summary>
    public SpriteSheet LoadSpriteSheet(string folder, string name, bool complex)
    {
        return SpriteSheet.Parse(ReadDataFile(folder, name + ".TAB"), ReadDataFile(folder, name + ".DAT"), complex);
    }

    /// <summary>Loads an uncompressed full-screen bitmap <c>{name}.DAT</c>.</summary>
    public IndexedImage LoadRawBitmap(string folder, string name, int width = 640, int height = 480)
    {
        return IndexedImage.FromRaw(ReadDataFile(folder, name + ".DAT"), width, height);
    }

    /// <summary>
    /// Loads the strings of a language, <c>DATA/LANG-{language}.DAT</c> (see the language numbers on
    /// <see cref="StringTable"/>), with CorsixTH's fixes for the German version.
    /// </summary>
    public StringTable LoadStrings(int language)
    {
        var strings = StringTable.Parse(ReadDataFile("DATA", $"LANG-{language}.DAT"));
        if (language == StringTable.German)
        {
            strings.FixGerman();
        }

        return strings;
    }

    /// <summary>The language numbers of the installed string files, in order.</summary>
    public IReadOnlyList<int> InstalledLanguages()
    {
        var languages = new List<int>();
        if (FindDirectory("DATA") is { } data)
        {
            foreach (var file in Directory.EnumerateFiles(data))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("LANG-", StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith(".DAT", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(name.AsSpan(5, name.Length - 9), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var language))
                {
                    languages.Add(language);
                }
            }
        }

        languages.Sort();
        return languages;
    }

    private FileNotFoundException NotFound(string[] segments)
    {
        var relative = string.Join('/', segments);
        return new FileNotFoundException($"'{relative}' was not found in the Theme Hospital folder '{RootPath}'.", relative);
    }

    /// <summary>Resolves each path segment case-insensitively; returns null if any segment is missing.</summary>
    private static string? ResolvePath(string root, IEnumerable<string> segments)
    {
        var current = root;
        foreach (var segment in segments)
        {
            if (segment.AsSpan().IndexOfAny('*', '?') >= 0
                || segment.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException($"'{segment}' is not a valid file or folder name.", nameof(segments));
            }

            var match = Directory.Exists(current)
                ? Directory.EnumerateFileSystemEntries(current, segment, CaseInsensitive).Order(StringComparer.Ordinal).FirstOrDefault()
                : null;
            if (match is null)
            {
                return null;
            }

            current = match;
        }

        return current;
    }

    private static void AddIfSet(List<string> list, string path)
    {
        if (!string.IsNullOrEmpty(path))
        {
            list.Add(path);
        }
    }
}
