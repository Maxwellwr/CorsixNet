// Where games are saved and how, after CorsixTH CorsixTH/Lua/app.lua (App:quickSave, App:quickLoad, the savegames
// folder) and world.lua (World:_executeAutosave and the monthly autosave in World:onEndDay).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Text;
using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Definitions;

namespace CorsixNet.Content.Saves;

/// <summary>A saved game read back: the level it was started on and the simulation.</summary>
public sealed record SavedGame(string LevelName, Simulation Simulation);

/// <summary>
/// The folder of saved games: the quick save (<c>quicksave.qs</c>, which the normal load and save screens do not show)
/// and the autosaves, one for each month of the year (<c>Autosaves/Autosave-mm-dd.sav</c>). A save file holds a short
/// header with the level's name, then the simulation's own save.
/// </summary>
public sealed class SaveGameStore
{
    /// <summary>The quick save's file name, without the usual extension so that the save screens leave it alone.</summary>
    public const string QuickSaveName = "quicksave.qs";

    /// <summary>The subfolder of the autosaves.</summary>
    public const string AutosaveFolder = "Autosaves";

    /// <summary>The environment variable that moves the saves folder.</summary>
    public const string DirectoryVariable = "CORSIXNET_SAVES";

    private static readonly byte[] Magic = "CNETSAVE"u8.ToArray();
    private const int HeaderVersion = 1;

    public SaveGameStore(string directory) => Directory = directory;

    /// <summary>The folder the saves go in.</summary>
    public string Directory { get; }

    public string QuickSavePath => Path.Combine(Directory, QuickSaveName);

    /// <summary>
    /// <see cref="DirectoryVariable"/> if set, otherwise a Saves folder in the user's application data, as CorsixTH's
    /// default savegame folder in its configuration folder.
    /// </summary>
    public static string DefaultDirectory() =>
        Environment.GetEnvironmentVariable(DirectoryVariable) is { Length: > 0 } given
            ? given
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CorsixNet", "Saves");

    /// <summary>The autosave's file name for a date (<c>Autosave-mm-dd.sav</c>), so a year keeps one per month.</summary>
    public static string AutosaveName(GameDate date) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Autosave-{date.Month:00}-{date.Day:00}.sav");

    public string AutosavePath(GameDate date) => Path.Combine(Directory, AutosaveFolder, AutosaveName(date));

    /// <summary>
    /// Whether a step from <paramref name="before"/> to <paramref name="after"/> began a day on which CorsixTH autosaves
    /// (<c>World:onEndDay</c>, after the day's end) at the given frequency: 0 never, 1 monthly (the last day of the
    /// month, the default), 2 weekly (also days 6, 13, 20 and 27), 3 daily.
    /// </summary>
    public static bool IsAutosaveDue(GameDate before, GameDate after, int frequency = 1) =>
        after.Hour == 0 && after != before && frequency switch
        {
            3 => true,
            2 => after.IsLastDayOfMonth || after.Day is 6 or 13 or 20 or 27,
            1 => after.IsLastDayOfMonth,
            _ => false,
        };

    /// <summary>Saves the game to <paramref name="path"/>; a file already there is only replaced once the save is written.</summary>
    public static void Save(string path, string levelName, Simulation simulation)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } folder)
        {
            System.IO.Directory.CreateDirectory(folder);
        }

        var temporary = path + ".tmp";
        using (var stream = File.Create(temporary))
        {
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Magic);
                writer.Write(HeaderVersion);
                writer.Write(levelName);
            }

            simulation.Save(stream);
        }

        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Reads a game saved by <see cref="Save"/>.</summary>
    /// <exception cref="InvalidDataException">The file is not a save, or one of another version.</exception>
    public static SavedGame Load(string path, GameContent content)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} is not a saved game.");
        }

        var version = reader.ReadInt32();
        if (version != HeaderVersion)
        {
            throw new InvalidDataException($"Saved game version {version} is not supported (expected {HeaderVersion}).");
        }

        var levelName = reader.ReadString();
        return new SavedGame(levelName, Simulation.Load(stream, content));
    }

    /// <summary>
    /// The newest saved game in the saves folder or its autosaves, or null when there is none (the main menu's Continue
    /// Game: <c>getMostRecentlyModifiedChildFile(".sav")</c>).
    /// </summary>
    public string? MostRecentSave() =>
        System.IO.Directory.Exists(Directory)
            ? System.IO.Directory.EnumerateFiles(Directory, "*.sav", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;

    /// <summary>The newest autosave, or null when there is none (CorsixTH's <c>--continue</c>).</summary>
    public string? MostRecentAutosave()
    {
        var folder = Path.Combine(Directory, AutosaveFolder);
        return System.IO.Directory.Exists(folder)
            ? System.IO.Directory.EnumerateFiles(folder, "*.sav").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
    }
}
