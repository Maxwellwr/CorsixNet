// The settings file, ported from CorsixTH CorsixTH/Lua/config_finder.lua (find_config, new_config_defaults,
// config_contents, load_config, save_config) for the settings CorsixNet uses so far.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using System.Text;
using CorsixNet.Content.Interface;
using CorsixNet.Formats;

namespace CorsixNet.Content.Settings;

/// <summary>
/// The player's settings, kept in <c>config.txt</c> in CorsixTH's format: Lua assignments such as
/// <c>width = 800</c> and <c>language = [[English]]</c>, with CorsixTH's setting names and defaults.
/// </summary>
/// <remarks>
/// Only the settings CorsixNet acts on are read and written; CorsixTH's others (announcements, music, movies, the
/// options dialog's game rules, ...) are left out until there is something they change.
/// </remarks>
public sealed record GameConfig
{
    /// <summary>The settings file's name.</summary>
    public const string FileName = "config.txt";

    /// <summary>An environment variable naming another settings file.</summary>
    public const string PathVariable = "CORSIXNET_CONFIG";

    /// <summary>CorsixTH's default game folder, which is a placeholder: the game is then looked for elsewhere.</summary>
    public const string PlaceholderInstall = @"X:\ThemeHospital\hospital";

    /// <summary>
    /// The language names and abbreviations of CorsixTH's languages that use the original strings, and the number of
    /// their <c>LANG-n.DAT</c> (CorsixTH's languages/*.lua: <c>Language(...)</c> and <c>Inherit("original_strings", n)</c>).
    /// </summary>
    private static readonly (string[] Names, int Number)[] OriginalLanguages =
    [
        (["English", "en", "eng"], StringTable.English),
        (["Français", "French", "fr", "fre", "fra"], StringTable.French),
        (["Deutsch", "German", "de", "ger", "deu"], StringTable.German),
        (["Italiano", "Italian", "it", "ita"], StringTable.Italian),
        (["Español", "Spanish", "es", "spa", "esp", "sp"], StringTable.Spanish),
        (["Svenska", "Swedish", "sv", "swe"], StringTable.Swedish),
    ];

    /// <summary>Whether the game starts full screen.</summary>
    public bool Fullscreen { get; init; }

    /// <summary>The window's width; CorsixTH's default is 800, but the debug status line wants more.</summary>
    public int Width { get; init; } = 1280;

    /// <summary>The window's height; CorsixTH's default is 600.</summary>
    public int Height { get; init; } = 800;

    /// <summary>The language of the game's texts, a name or an abbreviation as CorsixTH's settings take it.</summary>
    public string Language { get; init; } = "English";

    /// <summary>Whether earthquakes shake the screen.</summary>
    public bool EnableScreenShake { get; init; } = true;

    /// <summary>How often the game saves itself: 0 never, 1 monthly, 2 weekly, 3 daily.</summary>
    public int AutosaveFrequency { get; init; } = 1;

    /// <summary>The Theme Hospital folder; <see cref="PlaceholderInstall"/> or one that is not there means look for it.</summary>
    public string ThemeHospitalInstall { get; init; } = PlaceholderInstall;

    /// <summary>
    /// Whether a level from a map file (not a campaign's) is played without money (CorsixTH's <c>free_build_mode</c>).
    /// </summary>
    public bool FreeBuildMode { get; init; }

    /// <summary>Whether sound effects are played (<c>play_sounds</c>).</summary>
    public bool PlaySounds { get; init; } = true;

    /// <summary>The volume of sound effects, from 0 to 1 (<c>sound_volume</c>).</summary>
    public decimal SoundVolume { get; init; } = 0.5m;

    /// <summary>Whether the mouse at the screen's edge does not scroll the map (<c>prevent_edge_scrolling</c>).</summary>
    public bool PreventEdgeScrolling { get; init; }

    /// <summary>How much of a zoom carries on each tick, from 0 to 1 (<c>scrolling_momentum</c>).</summary>
    public decimal ScrollingMomentum { get; init; } = 0.8m;

    /// <summary>How fast the mouse wheel zooms (<c>zoom_speed</c>).</summary>
    public decimal ZoomSpeed { get; init; } = 80;

    /// <summary>How fast the arrow keys and the screen's edges scroll the map (<c>scroll_speed</c>).</summary>
    public decimal ScrollSpeed { get; init; } = 2;

    /// <summary>The same with Shift held (<c>shift_scroll_speed</c>).</summary>
    public decimal ShiftScrollSpeed { get; init; } = 4;

    /// <summary>The player's name in network games; empty for the default.</summary>
    public string PlayerName { get; init; } = string.Empty;

    /// <summary>
    /// The settings file: <see cref="PathVariable"/> if set; otherwise CorsixTH's <c>find_config</c> with this game's
    /// folder name: <c>%AppData%\CorsixNet</c> on Windows, else <c>$XDG_CONFIG_HOME/CorsixNet</c> or
    /// <c>~/.config/CorsixNet</c>.
    /// </summary>
    public static string DefaultPath()
    {
        if (Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } given)
        {
            return given;
        }

        string folder;
        if (OperatingSystem.IsWindows())
        {
            folder = Environment.GetEnvironmentVariable("AppData") ?? AppContext.BaseDirectory;
        }
        else
        {
            folder = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
                ? xdg
                : Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "~", ".config");
        }

        return Path.Combine(folder, "CorsixNet", FileName);
    }

    /// <summary>
    /// <c>load_config</c>: reads the settings file, writing one with the defaults first if there is none. Settings the
    /// file leaves out or gives a value of the wrong type keep their defaults.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a settings file.</exception>
    public static GameConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            var defaults = new GameConfig();
            try
            {
                defaults.Save(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // As CorsixTH, carry on with the defaults when the file can't be written.
            }

            return defaults;
        }

        return Parse(File.ReadAllText(path));
    }

    /// <summary>Reads the settings from a settings file's text.</summary>
    /// <exception cref="InvalidDataException">The text is not a settings file.</exception>
    public static GameConfig Parse(string text)
    {
        var values = LuaAssignments.Parse(text);
        var defaults = new GameConfig();
        bool Bool(string name, bool fallback) => values.GetValueOrDefault(name) is bool value ? value : fallback;
        int Int(string name, int fallback) => values.GetValueOrDefault(name) is decimal value && value == decimal.Truncate(value) && value is >= int.MinValue and <= int.MaxValue ? (int)value : fallback;
        decimal Number(string name, decimal fallback) => values.GetValueOrDefault(name) is decimal value ? value : fallback;
        string Text(string name, string fallback) => values.GetValueOrDefault(name) as string ?? fallback;
        return new GameConfig
        {
            Fullscreen = Bool("fullscreen", defaults.Fullscreen),
            Width = Int("width", defaults.Width),
            Height = Int("height", defaults.Height),
            Language = Text("language", defaults.Language),
            EnableScreenShake = Bool("enable_screen_shake", defaults.EnableScreenShake),
            AutosaveFrequency = Int("autosave_frequency", defaults.AutosaveFrequency),
            ThemeHospitalInstall = Text("theme_hospital_install", defaults.ThemeHospitalInstall),
            FreeBuildMode = Bool("free_build_mode", defaults.FreeBuildMode),
            PlaySounds = Bool("play_sounds", defaults.PlaySounds),
            SoundVolume = Number("sound_volume", defaults.SoundVolume),
            PreventEdgeScrolling = Bool("prevent_edge_scrolling", defaults.PreventEdgeScrolling),
            ScrollingMomentum = Number("scrolling_momentum", defaults.ScrollingMomentum),
            ZoomSpeed = Number("zoom_speed", defaults.ZoomSpeed),
            ScrollSpeed = Number("scroll_speed", defaults.ScrollSpeed),
            ShiftScrollSpeed = Number("shift_scroll_speed", defaults.ShiftScrollSpeed),
            PlayerName = Text("player_name", defaults.PlayerName),
        };
    }

    /// <summary><c>save_config</c>: writes the settings with CorsixTH's comments, creating the folder if needed.</summary>
    public void Save(string path)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } folder)
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllText(path, ToText());
    }

    /// <summary>The settings file's text (<c>config_contents</c>).</summary>
    public string ToText()
    {
        var text = new StringBuilder();
        text.Append("""
            ------------------------- CorsixNet configuration file -------------------------
            -- Lines starting with two dashes (like this one) are ignored.
            -- Text settings should have their values between double square braces, e.g.
            --  setting = [[value]]
            -- Number settings should not have anything around their value,
            -- e.g. setting = 42
            -- If you wish to go back to the default settings for everything, you can delete
            -- this text file and it will be re-created when you play the game.
            -- The settings and their meaning are CorsixTH's.
            -------------------------------------------------------------------------------
            -- Language to use for ingame text. Between the square braces should be one of:
            --  English  / en / eng
            --  French   / fr / fre / fra
            --  German   / de / ger / deu
            --  Italian  / it / ita
            --  Spanish  / es / spa
            --  Swedish  / sv / swe
            -- The game's own language files (LANG-n.DAT) are used, so the language must be
            -- installed; otherwise English, or the first one installed, is used.
            --

            """);
        text.Append(CultureInfo.InvariantCulture, $"language = {Quote(Language)}\n");
        text.Append("""

            -------------------------------------------------------------------------------
            -- Fullscreen mode. If this is set to false, then the game will run in a window.
            --

            """);
        text.Append(CultureInfo.InvariantCulture, $"fullscreen = {Lua(Fullscreen)}\n");
        text.Append("""

            -------------------------------------------------------------------------------
            -- Screen size. Must be at least 640 by 480.
            --

            """);
        text.Append(CultureInfo.InvariantCulture, $"width = {Width}\nheight = {Height}\n");
        text.Append("""

            -------------------------------------------------------------------------------
            -- Screen shake during earthquakes. If this is set to false, the screen keeps
            -- still.
            --

            """);
        text.Append(CultureInfo.InvariantCulture, $"enable_screen_shake = {Lua(EnableScreenShake)}\n");
        text.Append("""

            -------------------------------------------------------------------------------
            -- How often the game saves itself:
            --  0 never, 1 monthly (default), 2 weekly, 3 daily
            --

            """);
        text.Append(CultureInfo.InvariantCulture, $"autosave_frequency = {AutosaveFrequency}\n");
        text.Append("""

            -------------------------------------------------------------------------------
            -- Free build mode: levels from a map file (--map), but not campaign levels, are
            -- played without money. Building and staff cost nothing and there are no goals.
            --

            """);
        text.Append(CultureInfo.InvariantCulture, $"free_build_mode = {Lua(FreeBuildMode)}\n");
        text.Append("""

            -------------------------------------------------------------------------------
            -- Theme Hospital install folder: the folder with the game's DATA, LEVELS and
            -- QDATA folders. When it is not there, the game is looked for in the usual places.
            --

            """);
        text.Append(CultureInfo.InvariantCulture, $"theme_hospital_install = {Quote(ThemeHospitalInstall)}\n");
        text.Append("""

            -------------------------------------------------------------------------------
            -- Sounds: By default enabled and set at level 0.5
            --

            """);
        text.Append(CultureInfo.InvariantCulture, $"play_sounds = {Lua(PlaySounds)}\n");
        text.Append(CultureInfo.InvariantCulture, $"sound_volume = {Number(SoundVolume)}\n");
        text.Append("""

            -------------------------------------------------------------------------------
            -- Edge scrolling: By default enabled (prevent_edge_scrolling = false).
            --

            """);
        text.Append(CultureInfo.InvariantCulture, $"prevent_edge_scrolling = {Lua(PreventEdgeScrolling)}\n");
        text.Append("""

            -------------------------------------------------------------------------------
            -- Scrolling Momentum.
            -- Determines the amount of momentum when scrolling the map with the mouse.
            -- This should be a value between 0 and 1 where 0 is no momentum
            --

            """);
        text.Append(CultureInfo.InvariantCulture, $"scrolling_momentum = {Number(ScrollingMomentum)}\n");
        text.Append("""

            -------------------------------------------------------------------------------
            -- Zoom Speed: By default this is set at 80
            -- Any number value between 10 and 1000, 10 is very slow and 1000 is very fast!
            --

            """);
        text.Append(CultureInfo.InvariantCulture, $"zoom_speed = {Number(ZoomSpeed)}\n");
        text.Append("""

            -------------------------------------------------------------------------------
            -- Scroll Speeds: The speed of scrolling with and without shift being held.
            -- Any number value between 1 and 10, 1 is very slow and 10 is fast!
            --

            """);
        text.Append(CultureInfo.InvariantCulture, $"scroll_speed = {Number(ScrollSpeed)}\nshift_scroll_speed = {Number(ShiftScrollSpeed)}\n");
        text.Append("""

            -------------------------------------------------------------------------------
            -- Your name in network games, between the brace brackets like [[NAME]].
            --

            """);
        text.Append(CultureInfo.InvariantCulture, $"player_name = {Quote(PlayerName)}\n");
        return text.ToString();
    }

    /// <summary>The scrolling settings for <see cref="MapScrolling"/>.</summary>
    public ScrollSettings ScrollSettings() => new(
        (double)ScrollSpeed,
        (double)ShiftScrollSpeed,
        (double)ZoomSpeed,
        (double)ScrollingMomentum,
        PreventEdgeScrolling);

    /// <summary>
    /// The number of the original's string file for <see cref="Language"/>, or null for a language that does not use
    /// the original strings (CorsixTH has full translations for the others).
    /// </summary>
    public int? OriginalLanguageNumber()
    {
        foreach (var (names, number) in OriginalLanguages)
        {
            if (names.Any(name => string.Equals(name, Language.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return number;
            }
        }

        return null;
    }

    private static string Lua(bool value) => value ? "true" : "false";

    private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A Lua long string, with enough '=' signs that the text can't close it.</summary>
    private static string Quote(string value)
    {
        var level = string.Empty;
        while (value.Contains($"]{level}]", StringComparison.Ordinal) || value.EndsWith($"]{level}", StringComparison.Ordinal))
        {
            level += "=";
        }

        return $"[{level}[{value}]{level}]";
    }
}
