// The settings file, ported from CorsixTH CorsixTH/Lua/config_finder.lua (find_config, new_config_defaults,
// config_contents, load_config, save_config) for the settings CorsixNet uses so far, kept as YAML.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using System.Text;
using CorsixNet.Content.Interface;
using CorsixNet.Formats;

namespace CorsixNet.Content.Settings;

/// <summary>
/// The player's settings, kept in <c>config.yaml</c>: a YAML mapping such as <c>width: 800</c> and
/// <c>language: 'English'</c>, with CorsixTH's setting names and defaults. CorsixTH's <c>config.txt</c> (Lua) is turned
/// into one by <c>tools/CorsixNet.LuaConverter</c>.
/// </summary>
/// <remarks>
/// Only the settings CorsixNet acts on are read and written; CorsixTH's others (own music, the
/// options dialog's game rules, ...) are left out until there is something they change.
/// </remarks>
public sealed record GameConfig
{
    /// <summary>The settings file's name.</summary>
    public const string FileName = "config.yaml";


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

    /// <summary>Whether the adviser keeps quiet (CorsixTH's <c>adviser_disabled</c>).</summary>
    public bool AdviserDisabled { get; init; }

    /// <summary>Whether a room's description is shown the first time one is built (CorsixTH's <c>room_information_dialogs</c>).</summary>
    public bool RoomInformationDialogs { get; init; } = true;

    /// <summary>How often the game saves itself: 0 never, 1 monthly, 2 weekly, 3 daily.</summary>
    public int AutosaveFrequency { get; init; } = 1;

    /// <summary>The Theme Hospital folder; <see cref="PlaceholderInstall"/> or one that is not there means look for it.</summary>
    public string ThemeHospitalInstall { get; init; } = PlaceholderInstall;

    /// <summary>CorsixTH's default font file, which is a placeholder: the font is then looked for elsewhere.</summary>
    public const string PlaceholderUnicodeFont = @"X:\ThemeHospital\font.ttc";

    /// <summary>
    /// The TrueType font for languages the game's font cannot draw, such as Russian, Polish or Chinese (CorsixTH's
    /// <c>unicode_font</c>); <see cref="PlaceholderUnicodeFont"/> or one that is not there means look for one.
    /// </summary>
    public string UnicodeFont { get; init; } = PlaceholderUnicodeFont;

    /// <summary>
    /// The font file chosen for each language drawn with a TrueType font, by the language's name as in
    /// <see cref="Language"/> (<c>unicode_fonts</c>, CorsixNet's own); a language without one takes
    /// <see cref="UnicodeFont"/>, else a font of the system.
    /// </summary>
    public LanguageFonts LanguageFonts { get; init; } = LanguageFonts.None;

    /// <summary>The font file chosen for the language (any of its names), or null for none.</summary>
    public string? LanguageFont(IEnumerable<string> names) =>
        names.Select(name => LanguageFonts.GetValueOrDefault(name.Trim())).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));

    /// <summary>These settings with the font for the language set, or taken away (null or empty).</summary>
    public GameConfig WithLanguageFont(string language, string? path) => this with { LanguageFonts = LanguageFonts.With(language, path) };

    /// <summary>The folder of the player's own levels (CorsixTH's <c>levels</c>); empty for <see cref="UserFolder"/>'s default.</summary>
    public string Levels { get; init; } = string.Empty;

    /// <summary>The folder of the player's own campaigns (CorsixTH's <c>campaigns</c>); empty for <see cref="UserFolder"/>'s default.</summary>
    public string Campaigns { get; init; } = string.Empty;

    /// <summary>
    /// Whether a level from a map file (not a campaign's) is played without money (CorsixTH's <c>free_build_mode</c>).
    /// </summary>
    public bool FreeBuildMode { get; init; }

    /// <summary>
    /// Whether a room wrecked by an explosion can be cleared away for a fee (CorsixTH's <c>remove_destroyed_rooms</c>);
    /// in the original it stays for good.
    /// </summary>
    public bool RemoveDestroyedRooms { get; init; }

    /// <summary>
    /// Whether a new room comes with the extra objects the player's rooms of its kind have on average, to place and pay
    /// for (CorsixTH's <c>enable_avg_contents</c>); otherwise only the objects it needs.
    /// </summary>
    public bool EnableAverageContents { get; init; }

    /// <summary>
    /// Whether a payment by an insurance company floats up as a blue banknote rather than the original's green
    /// (CorsixTH's <c>enable_insurance_blue_banknote</c>, on by default).
    /// </summary>
    public bool EnableInsuranceBlueBanknote { get; init; } = true;

    /// <summary>Whether the bottom panel has CorsixTH's machine menu button (<c>machine_menu_button</c>, on by default).</summary>
    public bool MachineMenuButton { get; init; } = true;

    /// <summary>Whether sound effects are played (<c>play_sounds</c>).</summary>
    public bool PlaySounds { get; init; } = true;

    /// <summary>The volume of sound effects, from 0 to 1 (<c>sound_volume</c>).</summary>
    public decimal SoundVolume { get; init; } = 0.5m;

    /// <summary>Whether the announcer speaks (<c>play_announcements</c>).</summary>
    public bool PlayAnnouncements { get; init; } = true;

    /// <summary>Whether the announcer's words show as subtitles (<c>enable_announcer_subtitles</c>).</summary>
    public bool EnableAnnouncerSubtitles { get; init; }

    /// <summary>The announcer's volume, from 0 to 1 (<c>announcement_volume</c>).</summary>
    public decimal AnnouncementVolume { get; init; } = 0.5m;

    /// <summary>The MIDI API the music plays through (<c>midi_api</c>); empty for the default.</summary>
    public string MidiApi { get; init; } = string.Empty;

    /// <summary>The MIDI output of that API (<c>midi_port</c>); empty for the system's default.</summary>
    public string MidiPort { get; init; } = string.Empty;

    /// <summary>Whether the player's own music in <see cref="AudioMusic"/> plays instead of the game's (<c>play_own_music</c>).</summary>
    public bool PlayOwnMusic { get; init; }

    /// <summary>The folder of the player's own music (<c>audio_music</c>); empty for none.</summary>
    public string AudioMusic { get; init; } = string.Empty;

    /// <summary>The SoundFont the default MIDI synthesiser plays the music with (<c>soundfont</c>); empty to look for one.</summary>
    public string Soundfont { get; init; } = string.Empty;

    /// <summary>Whether the menu bar's clock shows 24 hours rather than AM and PM (<c>twentyfour_hour_clock</c>).</summary>
    public bool TwentyFourHourClock { get; init; } = true;

    /// <summary>The warmth colours at the start: 1 red, 2 blue green red, 3 yellow orange red (<c>warmth_colors_display_default</c>).</summary>
    public int WarmthColorsDisplayDefault { get; init; } = 1;

    /// <summary>Whether movies play at all (<c>movies</c>).</summary>
    public bool Movies { get; init; } = true;

    /// <summary>Whether the intro plays when the game starts (<c>play_intro</c>).</summary>
    public bool PlayIntro { get; init; } = true;

    /// <summary>Whether the demo movie plays when the main menu is left alone (<c>play_demo</c>).</summary>
    public bool PlayDemo { get; init; } = true;

    /// <summary>Whether the background music plays (<c>play_music</c>).</summary>
    public bool PlayMusic { get; init; } = true;

    /// <summary>The background music's volume, from 0 to 1 (<c>music_volume</c>).</summary>
    public decimal MusicVolume { get; init; } = 0.5m;

    /// <summary>Whether the mouse at the screen's edge does not scroll the map (<c>prevent_edge_scrolling</c>).</summary>
    public bool PreventEdgeScrolling { get; init; }

    /// <summary>Whether the game looks for a newer version on launch (<c>check_for_updates</c>).</summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>Whether the cursor is kept inside the game's window while it has focus (<c>capture_mouse</c>).</summary>
    public bool CaptureMouse { get; init; } = true;

    /// <summary>The folder games are saved to (<c>savegames</c>); empty for the default Saves folder.</summary>
    public string Savegames { get; init; } = string.Empty;

    /// <summary>
    /// Whether the right button drags the map as well as the middle one (<c>right_mouse_scrolling</c>). CorsixTH has it
    /// off; CorsixNet keeps it on to begin with, as it always let the right button drag.
    /// </summary>
    public bool RightMouseScrolling { get; init; } = true;

    /// <summary>The folder screenshots are saved to (<c>screenshots</c>); empty for "Screenshots" beside this file.</summary>
    public string Screenshots { get; init; } = string.Empty;

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

    /// <summary>The file beside the game that may say where the settings are (CorsixTH's <c>config.path.txt</c>).</summary>
    public const string PathFileName = "config.path.txt";

    /// <summary>
    /// What a <see cref="PathFileName"/> says: a path ending in a settings file's name (<c>.yaml</c>, <c>.yml</c> or, as
    /// CorsixTH has it, <c>.txt</c>) is the file; any other is the folder it is in; null when empty.
    /// </summary>
    public static string? FromPathFile(string contents)
    {
        var path = contents.Trim();
        if (path.Length == 0)
        {
            return null;
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".yaml" or ".yml" or ".txt" ? path : Path.Combine(path, FileName);
    }

    /// <summary>
    /// The settings file: <see cref="PathVariable"/> if set; else what a <see cref="PathFileName"/> beside the game says; otherwise CorsixTH's <c>find_config</c> with this game's
    /// folder name: <c>%AppData%\CorsixNet</c> on Windows, else <c>$XDG_CONFIG_HOME/CorsixNet</c> or
    /// <c>~/.config/CorsixNet</c>.
    /// </summary>
    public static string DefaultPath()
    {
        if (Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } given)
        {
            return given;
        }

        // config_finder.lua: a config.path.txt beside the game names the settings' folder, or the file itself.
        var pathFile = Path.Combine(AppContext.BaseDirectory, PathFileName);
        if (File.Exists(pathFile) && FromPathFile(File.ReadAllText(pathFile)) is { } named)
        {
            return named;
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
    /// <c>load_config</c>: reads the settings file, writing one with the defaults first if there is none. Settings
    /// the file leaves out or gives a value of the wrong type keep their defaults.
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
    public static GameConfig Parse(string text) => FromValues(SettingsYaml.Parse(text));


    internal static GameConfig FromValues(Dictionary<string, object?> values)
    {
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
            AdviserDisabled = Bool("adviser_disabled", defaults.AdviserDisabled),
            RoomInformationDialogs = Bool("room_information_dialogs", defaults.RoomInformationDialogs),
            AutosaveFrequency = Int("autosave_frequency", defaults.AutosaveFrequency),
            ThemeHospitalInstall = Text("theme_hospital_install", defaults.ThemeHospitalInstall),
            UnicodeFont = Text("unicode_font", defaults.UnicodeFont),
            LanguageFonts = values.GetValueOrDefault("unicode_fonts") is Dictionary<string, object?> fonts
                ? new LanguageFonts(fonts.Where(field => field.Value is string).Select(field => KeyValuePair.Create(field.Key, (string)field.Value!)))
                : defaults.LanguageFonts,
            Levels = Text("levels", defaults.Levels),
            Campaigns = Text("campaigns", defaults.Campaigns),
            FreeBuildMode = Bool("free_build_mode", defaults.FreeBuildMode),
            RemoveDestroyedRooms = Bool("remove_destroyed_rooms", defaults.RemoveDestroyedRooms),
            EnableAverageContents = Bool("enable_avg_contents", defaults.EnableAverageContents),
            EnableInsuranceBlueBanknote = Bool("enable_insurance_blue_banknote", defaults.EnableInsuranceBlueBanknote),
            MachineMenuButton = Bool("machine_menu_button", defaults.MachineMenuButton),
            PlaySounds = Bool("play_sounds", defaults.PlaySounds),
            SoundVolume = Number("sound_volume", defaults.SoundVolume),
            PlayAnnouncements = Bool("play_announcements", defaults.PlayAnnouncements),
            EnableAnnouncerSubtitles = Bool("enable_announcer_subtitles", defaults.EnableAnnouncerSubtitles),
            AnnouncementVolume = Number("announcement_volume", defaults.AnnouncementVolume),
            PlayMusic = Bool("play_music", defaults.PlayMusic),
            Movies = Bool("movies", defaults.Movies),
            TwentyFourHourClock = Bool("twentyfour_hour_clock", defaults.TwentyFourHourClock),
            WarmthColorsDisplayDefault = Math.Clamp(Int("warmth_colors_display_default", defaults.WarmthColorsDisplayDefault), 1, 3),
            MidiApi = Text("midi_api", defaults.MidiApi),
            MidiPort = Text("midi_port", defaults.MidiPort),
            Soundfont = Text("soundfont", defaults.Soundfont),
            PlayOwnMusic = Bool("play_own_music", defaults.PlayOwnMusic),
            AudioMusic = Text("audio_music", defaults.AudioMusic),
            PlayIntro = Bool("play_intro", defaults.PlayIntro),
            PlayDemo = Bool("play_demo", defaults.PlayDemo),
            MusicVolume = Number("music_volume", defaults.MusicVolume),
            PreventEdgeScrolling = Bool("prevent_edge_scrolling", defaults.PreventEdgeScrolling),
            CaptureMouse = Bool("capture_mouse", defaults.CaptureMouse),
            CheckForUpdates = Bool("check_for_updates", defaults.CheckForUpdates),
            Screenshots = Text("screenshots", defaults.Screenshots),
            Savegames = Text("savegames", defaults.Savegames),
            RightMouseScrolling = Bool("right_mouse_scrolling", defaults.RightMouseScrolling),
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
            #------------------------ CorsixNet configuration file -------------------------
            # Lines starting with # (like this one) are ignored. This is YAML.
            # Text settings have their values between single quotes, e.g.
            #  setting: 'value'
            # (a quote inside is written twice). Number settings have nothing around their
            # value, e.g. setting: 42; true and false are written as they are.
            # If you wish to go back to the default settings for everything, you can delete
            # this text file and it will be re-created when you play the game.
            # The settings and their meaning are CorsixTH's.
            #------------------------------------------------------------------------------
            # Language to use for ingame text. Between the square braces should be one of:
            #  English  / en / eng
            #  French   / fr / fre / fra
            #  German   / de / ger / deu
            #  Italian  / it / ita
            #  Spanish  / es / spa
            #  Swedish  / sv / swe
            # or one of CorsixTH's translations the game's font can draw:
            #  Brazilian Portuguese / pt_br / br    Danish / da / dk    Dutch / nl / dut
            #  Finnish / fi / fin    Norwegian / nb / nob    Portuguese / pt
            # or one drawn with a Unicode font (see unicode_font below):
            #  Russian / ru / rus    Ukrainian / uk / ukr    Polish / pl / pol
            #  Czech / cs / cze    Hungarian / hu / hun    Greek / el / gre
            #  Chinese (simplified) / zh(s)    Chinese (traditional) / zh(t)
            #  Japanese / ja / jp    Korean / ko / kor
            # The game's own language files (LANG-n.DAT) are used where installed, with
            # CorsixTH's translation laid over them; otherwise English, or the first one
            # installed, is used.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"language: {Quote(Language)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Fullscreen mode. If this is set to false, then the game will run in a window.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"fullscreen: {Yaml(Fullscreen)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Screen size. Must be at least 640 by 480.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"width: {Width}\nheight: {Height}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Screen shake during earthquakes. If this is set to false, the screen keeps
            # still.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"enable_screen_shake: {Yaml(EnableScreenShake)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Adviser. If set to true, the adviser does not give any advice.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"adviser_disabled: {Yaml(AdviserDisabled)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Room information dialogs. If set to true, a room's description is shown the
            # first time one is built.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"room_information_dialogs: {Yaml(RoomInformationDialogs)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # How often the game saves itself:
            #  0 never, 1 monthly (default), 2 weekly, 3 daily
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"autosave_frequency: {AutosaveFrequency}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Free build mode: levels from a map file (--map), but not campaign levels, are
            # played without money. Building and staff cost nothing and there are no goals.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"free_build_mode: {Yaml(FreeBuildMode)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # By default the player selects any extra objects they want for each room they
            # build. If you would like the game to remember what you usually add, then
            # change this option to true.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"enable_avg_contents: {Yaml(EnableAverageContents)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # By default patient payments made via an insurance company feature a blue
            # floating banknote animation, allowing for immediate visual distinction
            # from payments made directly to the hospital. If you prefer the original
            # display style, where all payments are in green, you can disable this option.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"enable_insurance_blue_banknote: {Yaml(EnableInsuranceBlueBanknote)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # By default machine menu is shown in a bottom panel. If you would like the
            # game to hide it change this option to false.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"machine_menu_button: {Yaml(MachineMenuButton)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # By default destroyed rooms can't be removed. If you would like the game to
            # give you the option of removing a destroyed room change this option to true.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"remove_destroyed_rooms: {Yaml(RemoveDestroyedRooms)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Theme Hospital install folder: the folder with the game's DATA, LEVELS and
            # QDATA folders. When it is not there, the game is looked for in the usual places.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"theme_hospital_install: {Quote(ThemeHospitalInstall)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Font file setting.
            # Specify a font file here if you wish to play the game in a language not
            # present in the original game. Examples include Russian, Chinese and Polish.
            # When it is not there, CorsixTHUnicode.ttf or GoNotoKurrent-Regular.ttf beside
            # the game is used, else a font of the system that has the language's letters.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"unicode_font: {Quote(UnicodeFont)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # A font file for each language, by the language's name, chosen in the
            # options window or written here, e.g.
            #  unicode_fonts:
            #    Russian: 'C:\Windows\Fonts\tahoma.ttf'
            # A language without one is drawn with the game's own font, or, when that
            # has not its letters, with unicode_font above, else a font of the system.
            #

            """);
        text.Append(LanguageFonts.Count > 0 ? "unicode_fonts:\n" : "unicode_fonts: {}\n");
        foreach (var (language, path) in LanguageFonts.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            text.Append(CultureInfo.InvariantCulture, $"  {Quote(language)}: {Quote(path)}\n");
        }
        text.Append("""

            #------------------------------------------------------------------------------
            # Folders of your own levels and campaigns, listed by the main menu's Single
            # Scenario and Custom Campaign. Left empty, they are the Levels and Campaigns
            # folders beside the saves folder.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"levels: {Quote(Levels)}\ncampaigns: {Quote(Campaigns)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Sounds: By default enabled and set at level 0.5
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"play_sounds: {Yaml(PlaySounds)}\n");
        text.Append(CultureInfo.InvariantCulture, $"sound_volume: {Number(SoundVolume)}\n");
        text.Append(CultureInfo.InvariantCulture, $"play_announcements: {Yaml(PlayAnnouncements)}\n");
        text.Append(CultureInfo.InvariantCulture, $"announcement_volume: {Number(AnnouncementVolume)}\n");
        text.Append(CultureInfo.InvariantCulture, $"enable_announcer_subtitles: {Yaml(EnableAnnouncerSubtitles)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Background music: By default enabled and set at level 0.5
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"play_music: {Yaml(PlayMusic)}\n");
        text.Append(CultureInfo.InvariantCulture, $"music_volume: {Number(MusicVolume)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Midi API and Device settings. Empty for the default; the sound settings
            # list the APIs and ports there are.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"midi_api: {Quote(MidiApi)}\nmidi_port: {Quote(MidiPort)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # SoundFont: the default MIDI synthesiser plays the music with the
            # GeneralUser-GS or FluidR3 SoundFont beside the game, or the system's, where
            # there is one; another SoundFont file (.sf2) can be given here.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"soundfont: {Quote(Soundfont)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Custom music. If you want to listen to your own music, set play_own_music to
            # true and audio_music to the folder of your music (OGG, MP3, WAV or MIDI
            # files). A file called "names.txt" with a file name on one row followed by the
            # name to show on the next names the songs.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"play_own_music: {Yaml(PlayOwnMusic)}\naudio_music: {Quote(AudioMusic)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Movie global on/off switch.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"movies: {Yaml(Movies)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # The menu bar's clock: 24 hours (true) or AM and PM (false).
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"twentyfour_hour_clock: {Yaml(TwentyFourHourClock)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Warmth Colors display settings.
            # This specifies which display method is set for warmth colours by default.
            # Possible values: 1 (Red), 2 (Blue Green Red) and 3 (Yellow Orange Red).
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"warmth_colors_display_default: {WarmthColorsDisplayDefault}\n");
        text.Append("""

            # Intro movie: Enabled by default
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"play_intro: {Yaml(PlayIntro)}\n");
        text.Append("""

            # Demo movie (played on idle at main menu): Enabled by default
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"play_demo: {Yaml(PlayDemo)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Edge scrolling: By default enabled (prevent_edge_scrolling = false).
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"prevent_edge_scrolling: {Yaml(PreventEdgeScrolling)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Capture mouse: By default enabled (capture_mouse = true).
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"capture_mouse: {Yaml(CaptureMouse)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Automatically check for updates.
            # If set to true, the game will automatically check for and alert you to newer
            # versions on startup.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"check_for_updates: {Yaml(CheckForUpdates)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Screenshots. By default, the "Screenshots" folder alongside this config file
            # is used for saving screenshots. Should this not be suitable, point this to a
            # folder which is more suitable.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"screenshots: {Quote(Screenshots)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Savegames. By default, the "Saves" folder is used for saved games. Point this
            # to another folder to keep them there instead.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"savegames: {Quote(Savegames)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Right Mouse Scrolling: whether the right mouse button drags the map as well as
            # the middle one (CorsixTH has it off; on here, as the game always had it).
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"right_mouse_scrolling: {Yaml(RightMouseScrolling)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Scrolling Momentum.
            # Determines the amount of momentum when scrolling the map with the mouse.
            # This should be a value between 0 and 1 where 0 is no momentum
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"scrolling_momentum: {Number(ScrollingMomentum)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Zoom Speed: By default this is set at 80
            # Any number value between 10 and 1000, 10 is very slow and 1000 is very fast!
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"zoom_speed: {Number(ZoomSpeed)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Scroll Speeds: The speed of scrolling with and without shift being held.
            # Any number value between 1 and 10, 1 is very slow and 10 is fast!
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"scroll_speed: {Number(ScrollSpeed)}\nshift_scroll_speed: {Number(ShiftScrollSpeed)}\n");
        text.Append("""

            #------------------------------------------------------------------------------
            # Your name in network games, between single quotes like 'NAME'.
            #

            """);
        text.Append(CultureInfo.InvariantCulture, $"player_name: {Quote(PlayerName)}\n");
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

    /// <summary>The name the settings give the original's string file <paramref name="number"/>, or null for one CorsixTH does not name.</summary>
    public static string? OriginalLanguageName(int number) =>
        OriginalLanguages.FirstOrDefault(language => language.Number == number).Names?[0];

    private static string Yaml(bool value) => value ? "true" : "false";

    private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// CorsixTH's <c>user_level_dir</c> and <c>user_campaign_dir</c>: the folder the settings give, or else the folder of
    /// <paramref name="name"/> (<c>Levels</c> or <c>Campaigns</c>) beside <paramref name="savesFolder"/>.
    /// </summary>
    public static string UserFolder(string configured, string savesFolder, string name) =>
        string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(savesFolder)) ?? savesFolder, name)
            : configured;

    /// <summary>A text as a YAML single-quoted scalar.</summary>
    private static string Quote(string value) => SettingsYaml.Quote(value);
}
