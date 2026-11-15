// The settings dialogs the options open in CorsixTH's look, ported from CorsixTH CorsixTH/Lua/dialogs/resizables:
// graphics_settings.lua (UIGraphics), customise.lua (UICustomise), sound_setting.lua (UISoundSettings) and
// menu_list_dialogs/accessibility.lua (UIAccessibility), laid out by resizable.lua's buildDialog. A setting applies at
// once and is saved, as CorsixTH's do; where CorsixTH drops down a list, the button goes to the next choice and the right
// button to the one before.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Content.Settings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>Which of the options' settings dialogs.</summary>
internal enum SettingsDialogKind
{
    Graphics,
    Customise,
    Sound,
    Accessibility,
}

/// <summary>
/// One of the options' dialogs: a title, its settings each with a button showing its value, and Back to the options
/// (<c>buttonBack</c>). Each change goes to <paramref name="apply"/> at once.
/// </summary>
/// <param name="kind">Which dialog.</param>
/// <param name="config">The settings as they are now.</param>
/// <param name="apply">Uses and saves new settings.</param>
/// <param name="back">Opens the options again.</param>
/// <param name="fonts">The fonts the language can be drawn with (the graphics dialog's font), or null.</param>
/// <param name="jukebox">Opens the jukebox (the sound dialog's button), or null without music.</param>
/// <param name="midiPorts">The system's MIDI outputs (the sound dialog's MIDI API and port), or null for none.</param>
internal sealed class SettingsDialog(SettingsDialogKind kind, GameConfig config, Action<GameConfig> apply, Action back, Func<string, LanguageFontChoices?>? fonts = null, Action? jukebox = null, Func<IReadOnlyList<string>>? midiPorts = null) : UiWindow
{
    /// <summary>graphics_settings.lua's window sizes for a screen of ordinary size.</summary>
    public static readonly (int Width, int Height)[] WindowSizes =
    [
        (640, 480), (800, 600), (1024, 768), (1280, 960), (1600, 1200), (1920, 1440), (1280, 1024), (1280, 720), (1366, 768),
        (1600, 900), (1920, 1080), (2560, 1440), (3840, 2160), (1280, 800), (1440, 900), (1680, 1050), (1920, 1200),
    ];

    /// <summary>The zoom speeds the accessibility dialog steps through (CorsixTH asks for a number from 10 to 1000).</summary>
    private static readonly decimal[] ZoomSpeeds = [10, 20, 40, 80, 160, 320, 640, 1000];

    private readonly ResizablePanels _panels = new();
    private GameConfig _config = config;

    public SettingsDialogKind Kind => kind;

    /// <summary>The settings as the dialog has made them.</summary>
    public GameConfig Config => _config;

    public override bool UsesResizableLook => true;

    public override string Title => UiText.Get(Strings + ".caption", kind.ToString());

    public override Vector2? DefaultPosition => new(0.5f, 0.25f);

    public override Vector2? FullscreenPosition => new(0.5f, 0.25f);

    public override bool IsSameAs(UiWindow other) => other is SettingsDialog dialog && dialog.Kind == kind;

    /// <summary>The dialog's section in the language files (<c>strings_ref</c>).</summary>
    private string Strings => kind switch
    {
        SettingsDialogKind.Graphics => "graphics_window",
        SettingsDialogKind.Customise => "customise_window",
        SettingsDialogKind.Sound => "audio_window",
        _ => "accessibility_window",
    };

    public override bool IsFullscreen(IWindowHost host) => ResizablePanels.IsAvailable(host);

    public override Point FullscreenSize => Build().Size;

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        _host = host;
        var (size, panels) = Build();
        _panels.Draw(batch, host, origin, size, panels, mouse);
    }

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous)
    {
        _host = host;
        _panels.Mouse(host, Build().Panels, local, mouse, previous);
    }

    public override string? FullscreenTooltip(IWindowHost host, Point local) => ResizablePanels.Tooltip(Build().Panels, local);

    public override Point? FindFullscreenButton(IWindowHost host, string text, int nth) => ResizablePanels.FindButton(Build().Panels, text, nth);

    /// <summary>Without CorsixTH's sprites: the same settings as a table, a button for each to step it.</summary>
    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        _host = host;
        foreach (var setting in Settings())
        {
            yield return WindowRow.Of(
                new WindowCell(setting.Label),
                WindowCell.Button(setting.Value, () => setting.Change(1), highlight: setting.On) with { Tooltip = setting.Tooltip });
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of([.. Navigation(host).Select(nav => WindowCell.Button(nav.Label, nav.Click) with { Tooltip = nav.Tooltip })]);
    }

    private IWindowHost? _host;

    private (Point Size, List<DialogPanel> Panels) Build() =>
        SettingsDialogLayout.Build(Title, Settings(), Navigation(_host), kind == SettingsDialogKind.Customise ? 165 : SettingsDialogLayout.ButtonWidth);

    private List<DialogNavButton> Navigation(IWindowHost? host)
    {
        var buttons = new List<DialogNavButton>();
        if (kind == SettingsDialogKind.Sound && jukebox is not null)
        {
            buttons.Add(new DialogNavButton(UiText.Get("audio_window.jukebox", "Jukebox"), jukebox, UiText.Get("tooltip.audio_window.jukebox", "Open the Jukebox to control the music")));
        }

        buttons.Add(new DialogNavButton(UiText.Get(kind == SettingsDialogKind.Sound ? "audio_window.back" : "options_window.back", "Back"), () =>
        {
            host?.Close(this);
            back();
        }, UiText.Get("tooltip.options_window.back", "Close the settings window")));
        return buttons;
    }

    /// <summary>The dialog's settings with their values now.</summary>
    public List<DialogSetting> Settings()
    {
        string Label(string name, string fallback) => UiText.Get(Strings + "." + name, fallback);
        string? Tip(string name) => UiText.Current?.Named("tooltip." + Strings + "." + name);
        DialogSetting Switch(string label, bool on, Func<bool, GameConfig> set, string? tooltip = null) =>
            new(label, on ? UiText.Get("options_window.option_on", "On") : UiText.Get("options_window.option_off", "Off"), _ => Apply(set(!on)), tooltip, On: on);

        switch (kind)
        {
            case SettingsDialogKind.Graphics:
                var size = string.Create(CultureInfo.InvariantCulture, $"{_config.Width}x{_config.Height}");
                List<DialogSetting> graphics =
                [
                    Switch(Label("fullscreen", "Fullscreen"), _config.Fullscreen, on => _config with { Fullscreen = on }, Tip("fullscreen")),
                    new(Label("window_size", "Window Size"), size, step => Apply(NextSize(step)), Tip("window_size")),
                ];
                if (fonts?.Invoke(_config.Language) is { Fonts.Count: > 0 } choices)
                {
                    graphics.Add(new(UiText.Get("folders_window.font_label", "Font"), FontName(choices), step => Apply(NextFont(choices, step))));
                }

                return graphics;
            case SettingsDialogKind.Customise:
                return
                [
                    Switch(Label("movies", "Movies"), _config.Movies, on => _config with { Movies = on }, Tip("movies")),
                    Switch(Label("play_intro", "Play Intro Movie"), _config.PlayIntro, on => _config with { PlayIntro = on }, Tip("play_intro")),
                    Switch(Label("enable_avg_contents", "Average Contents"), _config.EnableAverageContents, on => _config with { EnableAverageContents = on }, Tip("enable_avg_contents")),
                    Switch(Label("remove_destroyed_rooms", "Remove destroyed rooms"), _config.RemoveDestroyedRooms, on => _config with { RemoveDestroyedRooms = on }, Tip("remove_destroyed_rooms")),
                    Switch(Label("machine_menu_button", "Machine menu button"), _config.MachineMenuButton, on => _config with { MachineMenuButton = on }, Tip("machine_menu_button")),
                    Switch(Label("enable_insurance_blue_banknote", "Insurance Blue Banknote"), _config.EnableInsuranceBlueBanknote, on => _config with { EnableInsuranceBlueBanknote = on }, Tip("enable_insurance_blue_banknote")),
                    Switch(UiText.Get("corsixnet.options.adviser", "Adviser"), !_config.AdviserDisabled, on => _config with { AdviserDisabled = !on }),
                    Switch(UiText.Get("corsixnet.options.room_information", "Room information"), _config.RoomInformationDialogs, on => _config with { RoomInformationDialogs = on }),
                ];
            case SettingsDialogKind.Sound:
                List<DialogSetting> sound =
                [
                    Volume(Label("sound_volume", "Sound Volume"), _config.PlaySounds, _config.SoundVolume, (on, volume) => _config with { PlaySounds = on, SoundVolume = volume }, Tip("sound_volume")),
                    Volume(Label("announcement_volume", "Announcement Volume"), _config.PlayAnnouncements, _config.AnnouncementVolume, (on, volume) => _config with { PlayAnnouncements = on, AnnouncementVolume = volume }, Tip("announcement_volume")),
                    Volume(Label("music_volume", "Music Volume"), _config.PlayMusic, _config.MusicVolume, (on, volume) => _config with { PlayMusic = on, MusicVolume = volume }, Tip("music_volume")),

                    // play_own_music: only with a music folder set (play_own_music_no_folder_set otherwise).
                    _config.AudioMusic.Length > 0
                        ? Switch(Label("play_own_music", "Play Own Music"), _config.PlayOwnMusic, on => _config with { PlayOwnMusic = on }, Tip("play_own_music_button"))
                        : new(Label("play_own_music", "Play Own Music"), UiText.Get("options_window.option_off", "Off"), _ => { }, Tip("play_own_music_no_folder_set")),

                    // folder_settings.lua's music folder: its browse button opens UIDirectoryBrowser.
                    new(UiText.Get("folders_window.music_label", "Music"),
                        _config.AudioMusic.Length > 0 ? FolderName(_config.AudioMusic) : UiText.Get("tooltip.folders_window.not_specified", "No folder location specified yet!"),
                        _ => BrowseForMusic(),
                        _config.AudioMusic.Length > 0
                            ? UiText.Format("tooltip.folders_window.browse_music", "Browse for another location for your Music directory ( Current location: %1% ) ", _config.AudioMusic)
                            : UiText.Get("tooltip.folders_window.browse", "Browse for folder location")),
                ];
                if (midiPorts?.Invoke() is { Count: > 0 } ports)
                {
                    // The MIDI API (the default, or the platform's) and its port, the default first (midi_port_options).
                    List<string> apis = [string.Empty, Audio.WinMmMidiOutput.ApiName];
                    List<string> portChoices = [string.Empty, .. ports];
                    sound.Add(new(Label("midi_api", "MIDI API"), _config.MidiApi is { Length: > 0 } api ? api : Label("default_midi_api", "Default (Software)"),
                        step => Apply(_config with { MidiApi = Cycle(apis, _config.MidiApi, step) }), Tip("midi_api")));
                    sound.Add(new(Label("midi_port", "MIDI Port"), _config.MidiPort is { Length: > 0 } port ? port : Label("default_midi_port", "Default"),
                        step => Apply(_config with { MidiPort = Cycle(portChoices, _config.MidiPort, step) }), Tip("midi_port")));
                }

                return sound;
            default:
                return
                [
                    Switch(Label("enable_screen_shake", "Screen Shake"), _config.EnableScreenShake, on => _config with { EnableScreenShake = on }, Tip("enable_screen_shake")),
                    Switch(Label("enable_announcer_subtitles", "Announcer Subtitles"), _config.EnableAnnouncerSubtitles, on => _config with { EnableAnnouncerSubtitles = on }, Tip("enable_announcer_subtitles")),
                    new(Label("scroll_speed", "Scroll Speed"), Number(_config.ScrollSpeed), step => Apply(_config with { ScrollSpeed = Wrap(_config.ScrollSpeed + step, 1, 10) }), Tip("scroll_speed")),
                    new(Label("shift_scroll_speed", "Shift Scroll Speed"), Number(_config.ShiftScrollSpeed), step => Apply(_config with { ShiftScrollSpeed = Wrap(_config.ShiftScrollSpeed + step, 1, 10) }), Tip("shift_scroll_speed")),
                    new(Label("zoom_speed", "Zoom Speed"), Number(_config.ZoomSpeed), step => Apply(_config with { ZoomSpeed = Next(ZoomSpeeds, _config.ZoomSpeed, step) }), Tip("zoom_speed")),
                    Switch(UiText.Get("corsixnet.options.edge_scrolling", "Edge scrolling"), !_config.PreventEdgeScrolling, on => _config with { PreventEdgeScrolling = !on }),
                ];
        }
    }

    /// <summary>
    /// sound_setting.lua's volume buttons: off, then 10 % to 100 %; off turns the sound off (<c>play_sounds</c> and the
    /// like) and a volume turns it on.
    /// </summary>
    private DialogSetting Volume(string label, bool on, decimal volume, Func<bool, decimal, GameConfig> set, string? tooltip)
    {
        var level = on ? (int)Math.Clamp(Math.Round(volume * 10), 1, 10) : 0;
        var text = level == 0 ? UiText.Get("customise_window.option_off", "Off") : UiText.Get("menu_options_volume." + (level * 10).ToString(CultureInfo.InvariantCulture), (level * 10).ToString(CultureInfo.InvariantCulture) + "%");
        return new(label, text, step =>
        {
            var next = (level + step + 11) % 11;
            Apply(next == 0 ? set(false, volume) : set(true, next / 10m));
        }, tooltip, On: on);
    }

    private void Apply(GameConfig changed)
    {
        _config = changed;
        apply(changed);
    }

    /// <summary>A folder's own name, or the whole path for a drive.</summary>
    private static string FolderName(string path) => Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path;

    /// <summary>
    /// <c>UIFolder:buttonBrowseForAudio_music</c>: a folder with music in it becomes the music folder and the player's own
    /// music plays; another is refused with the reason, and the music folder is cleared.
    /// </summary>
    private void BrowseForMusic() => _host?.Open(new DirectoryBrowserWindow(
        UiText.Get("folders_window.music_location", "Select the directory you want to use for your Music. Upon submission, your music will start playing. To switch back to the Theme Hospital original soundtrack later, you can do so from Sound Settings."),
        ChooseMusic));

    /// <summary>The callback of <see cref="BrowseForMusic"/>, with <c>App:verifyPlayerMusicDir</c>'s check.</summary>
    internal void ChooseMusic(string path)
    {
        if (MusicFolderProblem(path) is { } problem)
        {
            // UIInformation: "//" starts a new paragraph.
            _host?.Open(new InformationWindow([.. problem.Split("//").Select(part => part.Trim()).Where(part => part.Length > 0)]));
            Apply(_config with { AudioMusic = string.Empty, PlayOwnMusic = false });
            return;
        }

        Apply(_config with { AudioMusic = path, PlayOwnMusic = true });
    }

    /// <summary>
    /// <c>App:verifyPlayerMusicDir</c>: null for a folder with at least one file the music plays (recorded or MIDI), else
    /// why it will not do (<c>errors.custom_music_bad_path</c>, <c>errors.custom_music_no_tracks</c>).
    /// </summary>
    public static string? MusicFolderProblem(string path)
    {
        if (!Directory.Exists(path))
        {
            return UiText.Format("errors.custom_music_bad_path", "Custom Music directory chosen is not a valid directory. Please check the directory and try again.// %s", path);
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return UiText.Format("errors.custom_music_bad_path", "Custom Music directory chosen is not a valid directory. Please check the directory and try again.// %s", path);
        }

        var playable = Audio.PcmSources.Extensions.Concat(Audio.BackgroundMusic.InstructionalExtensions).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return files.Any(file => playable.Contains(Path.GetExtension(file).TrimStart('.')))
            ? null
            : UiText.Format("errors.custom_music_no_tracks", "Custom Music directory chosen contains no playable music files. Please check the directory and try again.// %s", path);
    }

    private GameConfig NextSize(int step)
    {
        var index = Array.FindIndex(WindowSizes, size => size.Width == _config.Width && size.Height == _config.Height);
        var next = index < 0 ? 0 : (index + step + WindowSizes.Length) % WindowSizes.Length;
        return _config with { Width = WindowSizes[next].Width, Height = WindowSizes[next].Height };
    }

    /// <summary>The font chosen for the language, or the choices' default when none is (as the options' font row).</summary>
    private string FontName(LanguageFontChoices choices)
    {
        if (_config.LanguageFont([_config.Language]) is not { } path)
        {
            return choices.Default switch
            {
                LanguageFontChoices.GameFont => UiText.Get("corsixnet.options.game_font", LanguageFontChoices.GameFont),
                LanguageFontChoices.SystemFont => UiText.Get("corsixnet.options.system_font", LanguageFontChoices.SystemFont),
                var other => other,
            };
        }

        return choices.Fonts.FirstOrDefault(choice => string.Equals(choice.Path, path, StringComparison.OrdinalIgnoreCase)).Name ?? Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>The next or previous font for the language, the default (none chosen) coming before the first.</summary>
    private GameConfig NextFont(LanguageFontChoices choices, int step)
    {
        List<string?> paths = [null, .. choices.Fonts.Select(choice => choice.Path)];
        var chosen = _config.LanguageFont([_config.Language]);
        var index = paths.FindIndex(path => string.Equals(path, chosen, StringComparison.OrdinalIgnoreCase));
        var next = ((index < 0 ? 0 : index) + step + paths.Count) % paths.Count;
        return _config.WithLanguageFont(_config.Language, paths[next]);
    }

    private static string Cycle(List<string> choices, string current, int step)
    {
        var index = choices.FindIndex(choice => string.Equals(choice, current, StringComparison.Ordinal));
        return choices[index < 0 ? 0 : (index + step + choices.Count) % choices.Count];
    }

    private static decimal Wrap(decimal value, decimal low, decimal high) => value < low ? high : value > high ? low : value;

    private static decimal Next(decimal[] values, decimal current, int step)
    {
        var index = Array.IndexOf(values, current);
        return values[index < 0 ? 0 : (index + step + values.Length) % values.Length];
    }

    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
