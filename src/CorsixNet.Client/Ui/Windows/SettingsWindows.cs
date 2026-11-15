using System.Globalization;
using CorsixNet.Content.Saves;
using CorsixNet.Content.Settings;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The save and load windows (CorsixTH's <c>dialogs/resizables/file_browsers/save_game.lua</c> and
/// <c>load_game.lua</c>), from the menu (Esc): the saved games in the saves folder, newest first,
/// with the quick save and the autosaves; saving to a new file or over one, and loading one.
/// </summary>
internal sealed class SaveLoadWindow(SaveGameStore store, bool saving, Func<string, bool> save, Func<string, bool> load) : UiWindow, IKeyCaptureWindow
{
    /// <summary>The longest name typed for a new save.</summary>
    public const int MaxNameLength = 40;

    private bool _typing;
    private string _name = string.Empty;

    /// <summary>The save's name being typed (save_game.lua's new savegame box), while it waits for keys.</summary>
    public string TypedName => _name;

    public bool IsCapturing => _typing;

    /// <summary>
    /// The new savegame box: letters (capitals with Shift), digits, spaces, - and _ are typed; Backspace takes the last
    /// one away; Return saves under the name, asking first if a save of that name exists; Escape stops typing.
    /// </summary>
    public void Capture(Hotkey key)
    {
        switch (key.Key)
        {
            case "escape":
                _typing = false;
                return;
            case "backspace":
                _name = _name.Length > 0 ? _name[..^1] : _name;
                return;
            case "return":
                _typing = false;
                _pendingSave = _name.Trim().Length > 0 ? _name.Trim() + Extension : null;
                return;
            case "space":
                Type(' ');
                return;
            case "-":
                Type(key.Shift ? '_' : '-');
                return;
        }

        if (key.Key.Length == 1 && char.IsAsciiLetterOrDigit(key.Key[0]))
        {
            Type(key.Shift && char.IsAsciiLetter(key.Key[0]) ? char.ToUpperInvariant(key.Key[0]) : key.Key[0]);
        }
    }

    private void Type(char character)
    {
        if (_name.Length < MaxNameLength)
        {
            _name += character;
        }
    }

    /// <summary>The file name Return asked to save under, done on the window's next look.</summary>
    private string? _pendingSave;
    public const string Extension = ".sav";

    private string? _confirmOverwrite;

    public override string Title => saving ? UiText.Label("save_game_window.caption", "Save game") : UiText.Label("load_game_window.caption", "Load game");

    public override bool IsSameAs(UiWindow other) => other is SaveLoadWindow;

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        if (saving && _pendingSave is { } typed)
        {
            _pendingSave = null;
            var target = Path.Combine(store.Directory, typed);
            if (File.Exists(target))
            {
                _confirmOverwrite = target;
            }
            else if (save(target))
            {
                host.Close(this);
                yield break;
            }
        }

        if (saving)
        {
            yield return WindowRow.Of(WindowCell.Button(_typing ? $"Name: {_name}_" : _name.Length > 0 ? $"Name: {_name} (click to edit, Return to save)" : "Type a name for a new save", () => _typing = !_typing, highlight: _typing));
            var date = host.Simulation.Date;
            var name = UiText.Format("corsixnet.save_game.file_name", "Year %1% month %2% day %3%", date.Year, date.Month, date.Day) + Extension;
            yield return WindowRow.Of(WindowCell.Button(UiText.Format("corsixnet.save_game.save_as", "Save as \"%1%\"", name), () =>
            {
                if (save(Path.Combine(store.Directory, name)))
                {
                    host.Close(this);
                }
            }));
            yield return WindowRow.Gap;
        }

        var games = Games().ToList();
        if (games.Count == 0)
        {
            yield return WindowCell.Line(UiText.Get("corsixnet.save_game.none", "No saved games yet."), dim: true);
            yield break;
        }

        yield return WindowRow.Of(UiText.Get("corsixnet.save_game.name", "Saved game"), UiText.Get("corsixnet.save_game.saved_at", "Saved at"), string.Empty);
        foreach (var (path, label) in games)
        {
            var when = File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            WindowCell action;
            if (!saving)
            {
                action = WindowCell.Button(UiText.Get("load_game_window.load_button", "Load"), () =>
                {
                    if (load(path))
                    {
                        host.Close(this);
                    }
                });
            }
            else if (_confirmOverwrite == path)
            {
                action = WindowCell.Button(UiText.Get("corsixnet.save_game.confirm_overwrite", "Sure? Overwrite"), () =>
                {
                    _confirmOverwrite = null;
                    if (save(path))
                    {
                        host.Close(this);
                    }
                });
            }
            else
            {
                action = WindowCell.Button(UiText.Get("corsixnet.save_game.overwrite", "Overwrite"), () => _confirmOverwrite = path, enabled: path != store.QuickSavePath);
            }

            yield return WindowRow.Of(label, when, action);
        }
    }

    /// <summary>The saved games, newest first: manual saves, the quick save and the autosaves.</summary>
    private IEnumerable<(string Path, string Label)> Games()
    {
        var found = new List<(string Path, string Label)>();
        if (Directory.Exists(store.Directory))
        {
            found.AddRange(Directory.EnumerateFiles(store.Directory, "*" + Extension).Select(path => (path, Path.GetFileNameWithoutExtension(path))));
        }

        if (File.Exists(store.QuickSavePath))
        {
            found.Add((store.QuickSavePath, UiText.Get("corsixnet.save_game.quick_save", "Quick save")));
        }

        var autosaves = Path.Combine(store.Directory, SaveGameStore.AutosaveFolder);
        if (Directory.Exists(autosaves))
        {
            found.AddRange(Directory.EnumerateFiles(autosaves, "*" + Extension).Select(path => (path, Path.GetFileNameWithoutExtension(path))));
        }

        return found.OrderByDescending(game => File.GetLastWriteTimeUtc(game.Path));
    }
}

/// <summary>The fonts a language can be drawn with in the options window, and the name of its font when none is chosen.</summary>
internal sealed record LanguageFontChoices(string Default, IReadOnlyList<(string Name, string Path)> Fonts)
{
    /// <summary>The default of a language the game's bitmap font draws.</summary>
    public const string GameFont = "Game font";

    /// <summary>The default of a language drawn with a TrueType font found among the system's.</summary>
    public const string SystemFont = "System";
}

/// <summary>
/// The options window (CorsixTH's <c>dialogs/resizables/options.lua</c> with <c>customise.lua</c> and
/// <c>sound_setting.lua</c>): sound effects and their volume, screen shake, edge scrolling, scroll speeds and the
/// autosave frequency, and the language among the installed ones (CorsixTH's <c>options.lua</c> language list). Changes
/// are kept in a draft until Apply, which saves <c>config.yaml</c> and uses them at once; the screen size takes effect on
/// the next start. Every language gets a choice of font among the system's fonts that have its letters
/// (<c>unicode_fonts</c>): a language the game's bitmap font draws keeps that font by default ("Game font"), one drawn
/// with a TrueType font leaves it to be found as before ("System"). The language and the font are
/// tried out as soon as they are stepped to, as CorsixTH's language list changes the language on a click; closing the
/// window without Apply gives the ones it opened with back.
/// </summary>
/// <param name="fonts">
/// The fonts the language can be drawn with, by name, and the name of what it is drawn with when none is chosen; the
/// fonts are empty while they are still being looked for, and null leaves the font row out.
/// </param>
/// <param name="preview">
/// Shows the game in a language and font without saving them: the settings the window opened with but for the draft's
/// language and fonts, or those it opened with when it closes without Apply.
/// </param>
internal sealed class OptionsWindow(GameConfig current, Action<GameConfig> apply, IReadOnlyList<string>? languages = null, Action? hotkeys = null, Func<string, LanguageFontChoices?>? fonts = null, Action<GameConfig>? preview = null) : UiWindow
{
    private readonly IReadOnlyList<string> _languages = languages ?? [];

    private readonly GameConfig _current = current;
    private GameConfig _draft = current;

    /// <summary>Whether the game shows a language or font other than the one the window opened with.</summary>
    private bool _previewing;

    private bool _applied;

    public override string Title => UiText.Get("options_window.caption", "Options");

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        yield return Switch(UiText.Get("corsixnet.options.sound_effects", "Sound effects"), _draft.PlaySounds, on => _draft = _draft with { PlaySounds = on });
        yield return Stepper(UiText.Get("audio_window.sound_volume", "Sound volume"), Percent(_draft.SoundVolume), () => _draft = _draft with { SoundVolume = Math.Max(0m, _draft.SoundVolume - 0.1m) }, () => _draft = _draft with { SoundVolume = Math.Min(1m, _draft.SoundVolume + 0.1m) });
        yield return Switch(UiText.Get("accessibility_window.enable_screen_shake", "Screen shake"), _draft.EnableScreenShake, on => _draft = _draft with { EnableScreenShake = on });
        yield return Switch(UiText.Get("corsixnet.options.adviser", "Adviser"), !_draft.AdviserDisabled, on => _draft = _draft with { AdviserDisabled = !on });
        yield return Switch(UiText.Get("corsixnet.options.room_information", "Room information"), _draft.RoomInformationDialogs, on => _draft = _draft with { RoomInformationDialogs = on });
        yield return Switch(UiText.Get("corsixnet.options.edge_scrolling", "Edge scrolling"), !_draft.PreventEdgeScrolling, on => _draft = _draft with { PreventEdgeScrolling = !on });
        yield return Stepper(UiText.Get("accessibility_window.scroll_speed", "Scroll speed"), _draft.ScrollSpeed.ToString(CultureInfo.InvariantCulture), () => _draft = _draft with { ScrollSpeed = Math.Max(1, _draft.ScrollSpeed - 1) }, () => _draft = _draft with { ScrollSpeed = Math.Min(10, _draft.ScrollSpeed + 1) });
        yield return Stepper(UiText.Get("accessibility_window.shift_scroll_speed", "Shift scroll speed"), _draft.ShiftScrollSpeed.ToString(CultureInfo.InvariantCulture), () => _draft = _draft with { ShiftScrollSpeed = Math.Max(1, _draft.ShiftScrollSpeed - 1) }, () => _draft = _draft with { ShiftScrollSpeed = Math.Min(10, _draft.ShiftScrollSpeed + 1) });
        yield return Stepper(UiText.Get("options_window.autosave_frequency", "Autosave"), _draft.AutosaveFrequency switch { 0 => UiText.Get("options_window.option_off", "off"), 1 => UiText.Get("autosave_frequency.monthly", "monthly"), 2 => UiText.Get("autosave_frequency.weekly", "weekly"), _ => UiText.Get("autosave_frequency.daily", "daily") }, () => _draft = _draft with { AutosaveFrequency = Math.Max(0, _draft.AutosaveFrequency - 1) }, () => _draft = _draft with { AutosaveFrequency = Math.Min(3, _draft.AutosaveFrequency + 1) });
        // customise.lua's switches for the settings this game has.
        yield return Switch(UiText.Get("customise_window.remove_destroyed_rooms", "Remove destroyed rooms"), _draft.RemoveDestroyedRooms, on => _draft = _draft with { RemoveDestroyedRooms = on }, UiText.Get("tooltip.customise_window.remove_destroyed_rooms", "If you would like to be able to remove destroyed rooms for a fee, turn this option on"));
        yield return Switch(UiText.Get("customise_window.enable_avg_contents", "Average Contents"), _draft.EnableAverageContents, on => _draft = _draft with { EnableAverageContents = on }, UiText.Get("tooltip.customise_window.enable_avg_contents", "If you would like the game to remember which extra objects you usually add while building rooms, turn this option on"));
        yield return Switch(UiText.Get("customise_window.enable_insurance_blue_banknote", "Insurance Blue Banknote"), _draft.EnableInsuranceBlueBanknote, on => _draft = _draft with { EnableInsuranceBlueBanknote = on }, UiText.Get("tooltip.customise_window.enable_insurance_blue_banknote", "Patient payments made via an insurance company feature a blue floating banknote animation. If you prefer the original display style, where all payments are in green, you can disable this option"));
        // sound_setting.lua's announcement volume and accessibility.lua's subtitles, with the announcements' own switch.
        yield return Switch(UiText.Get("corsixnet.options.announcements", "Announcements"), _draft.PlayAnnouncements, on => _draft = _draft with { PlayAnnouncements = on });
        yield return Stepper(UiText.Get("audio_window.announcement_volume", "Announcement volume"), Percent(_draft.AnnouncementVolume), () => _draft = _draft with { AnnouncementVolume = Math.Max(0m, _draft.AnnouncementVolume - 0.1m) }, () => _draft = _draft with { AnnouncementVolume = Math.Min(1m, _draft.AnnouncementVolume + 0.1m) });
        yield return Switch(UiText.Get("accessibility_window.enable_announcer_subtitles", "Announcer subtitles"), _draft.EnableAnnouncerSubtitles, on => _draft = _draft with { EnableAnnouncerSubtitles = on }, UiText.Get("tooltip.accessibility_window.enable_announcer_subtitles", "If you would like the game to display subtitles for your hospital's announcements, turn this option on"));
        var size = string.Create(CultureInfo.InvariantCulture, $"{_draft.Width} x {_draft.Height}");
        yield return WindowRow.Of(UiText.Get("corsixnet.options.screen", "Screen"), _draft.Fullscreen ? UiText.Format("corsixnet.options.screen_fullscreen", "%1%, fullscreen", size) : size);
        var language = UiText.Get("options_window.language", "Language");
        yield return _languages.Count > 1
            ? Stepper(language, _draft.Language, () => Preview(() => StepLanguage(-1)), () => Preview(() => StepLanguage(+1)))
            : WindowRow.Of(language, _draft.Language);
        if (fonts?.Invoke(_draft.Language) is { } choices)
        {
            yield return Stepper(UiText.Get("folders_window.font_label", "Font"), FontName(choices), () => Preview(() => StepFont(fonts(_draft.Language)?.Fonts ?? [], -1)), () => Preview(() => StepFont(fonts(_draft.Language)?.Fonts ?? [], +1)));
        }
        if (hotkeys is not null)
        {
            // buttonHotkey: the options close for the hotkey assignment dialog, which opens them again.
            var assign = WindowCell.Button(UiText.Get("corsixnet.options.assign", "Assign"), () =>
            {
                host.Close(this);
                hotkeys();
            });
            yield return WindowRow.Of(UiText.Get("options_window.hotkey", "Hotkeys"), assign with { Tooltip = UiText.Get("tooltip.options_window.hotkey", "Change keyboard hotkeys.") });
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(
            WindowCell.Button(UiText.Get("graphics_window.apply", "Apply"), () =>
            {
                _applied = true;
                apply(_draft);
                host.Close(this);
            }, _draft != _current),
            WindowCell.Button(UiText.Get("graphics_window.cancel", "Cancel"), () => host.Close(this)));
    }

    /// <summary>Gives back the language and font the window opened with unless the draft was applied.</summary>
    public override void Closed()
    {
        if (_previewing && !_applied)
        {
            _previewing = false;
            preview?.Invoke(_current);
        }
    }

    /// <summary>Steps the language or font and shows the game in it at once.</summary>
    private void Preview(Action step)
    {
        var before = (_draft.Language, _draft.LanguageFonts);
        step();
        if (preview is null || (_draft.Language, _draft.LanguageFonts).Equals(before))
        {
            return;
        }

        _previewing = true;
        preview(_current with { Language = _draft.Language, LanguageFonts = _draft.LanguageFonts });
    }

    /// <summary>The next or previous installed language; a language not on the list goes to its first.</summary>
    private void StepLanguage(int step)
    {
        var index = -1;
        for (var i = 0; i < _languages.Count; i++)
        {
            if (string.Equals(_languages[i], _draft.Language.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                index = i;
            }
        }

        var next = index < 0 ? 0 : (index + step + _languages.Count) % _languages.Count;
        _draft = _draft with { Language = _languages[next] };
    }

    /// <summary>The name of the font chosen for the draft's language, or the choices' default for none.</summary>
    private string FontName(LanguageFontChoices choices)
    {
        if (_draft.LanguageFont([_draft.Language]) is not { } path)
        {
            return choices.Default switch
            {
                LanguageFontChoices.GameFont => UiText.Get("corsixnet.options.game_font", LanguageFontChoices.GameFont),
                LanguageFontChoices.SystemFont => UiText.Get("corsixnet.options.system_font", LanguageFontChoices.SystemFont),
                var other => other,
            };
        }

        foreach (var (name, choice) in choices.Fonts)
        {
            if (string.Equals(choice, path, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>The next or previous font for the draft's language, the default (none chosen) coming before the first.</summary>
    private void StepFont(IReadOnlyList<(string Name, string Path)> choices, int step)
    {
        if (choices.Count == 0)
        {
            return;
        }

        List<string?> paths = [null, .. choices.Select(choice => choice.Path)];
        var chosen = _draft.LanguageFont([_draft.Language]);
        var index = paths.FindIndex(path => string.Equals(path, chosen, StringComparison.OrdinalIgnoreCase));
        var next = ((index < 0 ? 0 : index) + step + paths.Count) % paths.Count;
        _draft = _draft.WithLanguageFont(_draft.Language, paths[next]);
    }

    private static string Percent(decimal fraction) => string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Round(fraction * 100)} %");

    private static WindowRow Switch(string label, bool on, Action<bool> set, string? tooltip = null) =>
        WindowRow.Of(
            new WindowCell(label) { Tooltip = tooltip },
            WindowCell.Button(UiText.Get("options_window.option_on", "On"), () => set(true), !on, on),
            WindowCell.Button(UiText.Get("options_window.option_off", "Off"), () => set(false), on, !on));

    private static WindowRow Stepper(string label, string value, Action down, Action up) =>
        WindowRow.Of(label, WindowCell.Button("-", down), WindowCell.Number(value), WindowCell.Button("+", up));
}

/// <summary>
/// The menu (CorsixTH's top menu bar, <c>dialogs/menu.lua</c>, which Esc shows): saving, loading, restarting and
/// quitting, the options, the charts and the debug windows.
/// </summary>
internal sealed class MenuWindow(Action<string> action) : UiWindow
{
    public override string Title => UiText.Get("corsixnet.menu.caption", "Menu");

    public override string? Hotkey => "Esc";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        yield return WindowRow.Of(
            UiText.Get("corsixnet.menu.file", "File"),
            Item(UiText.Get("corsixnet.menu.new_game", "New game"), "newgame"),
            Item(UiText.Get("corsixnet.menu.save", "Save"), "save"),
            Item(UiText.Get("corsixnet.menu.load", "Load"), "load"),
            Item(UiText.Get("corsixnet.menu.quick_save", "Quick save"), "quicksave"),
            Item(UiText.Get("corsixnet.menu.quick_load", "Quick load"), "quickload"),
            Item(UiText.Get("corsixnet.menu.restart", "Restart"), "restart"),
            Item(UiText.Get("corsixnet.menu.quit", "Quit"), "quit"));
        yield return WindowRow.Of(UiText.Get("corsixnet.menu.replays", "Replays"), Item(UiText.Get("corsixnet.menu.save_replay", "Save replay"), "savereplay"), Item(UiText.Get("corsixnet.menu.watch_replay", "Watch replay"), "replays"));
        yield return WindowRow.Of(UiText.Get("corsixnet.menu.options", "Options"), Item(UiText.Get("corsixnet.menu.settings", "Settings"), "options"), Item(UiText.Get("corsixnet.menu.levels", "Levels"), "levels"));
        yield return WindowRow.Of(
            UiText.Get("corsixnet.menu.charts", "Charts"),
            WindowCell.Button(UiText.Get("corsixnet.menu.bank", "Bank"), () => host.Open(new BankManagerWindow())),
            WindowCell.Button(UiText.Get("corsixnet.menu.statement", "Statement"), () => host.Open(new BankStatementWindow())),
            WindowCell.Button(UiText.Get("corsixnet.menu.staff", "Staff"), () => host.Open(new StaffManagementWindow())),
            WindowCell.Button(UiText.Get("corsixnet.menu.town_map", "Town map"), () => host.Open(new TownMapWindow())),
            WindowCell.Button(UiText.Get("corsixnet.menu.casebook", "Casebook"), () => host.Open(new CasebookWindow())),
            WindowCell.Button(UiText.Get("corsixnet.menu.research", "Research"), () => host.Open(new ResearchWindow())));
        yield return WindowRow.Of(
            string.Empty,
            WindowCell.Button(UiText.Get("corsixnet.menu.status", "Status"), () => host.Open(new ProgressReportWindow())),
            WindowCell.Button(UiText.Get("corsixnet.menu.graphs", "Graphs"), () => host.Open(new GraphWindow())),
            WindowCell.Button(UiText.Get("corsixnet.menu.policy", "Policy"), () => host.Open(new PolicyWindow())),
            WindowCell.Button(UiText.Get("corsixnet.menu.annual_report", "Annual report"), () => host.Open(new AnnualReportWindow())));
        yield return WindowRow.Of(UiText.Get("corsixnet.menu.debug", "Debug"), WindowCell.Button(UiText.Get("corsixnet.menu.cheats", "Cheats"), () => host.Open(new CheatsWindow())));

        WindowCell Item(string text, string name) => WindowCell.Button(text, () =>
        {
            host.Close(this);
            action(name);
        });
    }
}

/// <summary>The replays in the saves folder, newest first, to watch one.</summary>
internal sealed class ReplayListWindow(string folder, Action<string> watch) : UiWindow
{
    public override string Title => UiText.Get("corsixnet.menu.replays", "Replays");

    /// <summary>The replay files in the folder, newest first.</summary>
    public static IReadOnlyList<string> Files(string folder) =>
        Directory.Exists(folder)
            ? [.. Directory.EnumerateFiles(folder, "*" + Content.Saves.Replay.Extension).OrderByDescending(File.GetLastWriteTimeUtc)]
            : [];

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var files = Files(folder);
        if (files.Count == 0)
        {
            yield return WindowCell.Line(UiText.Get("corsixnet.replays.none", "No replays yet: Save replay in the menu keeps the game played so far."), dim: true);
            yield break;
        }

        foreach (var path in files)
        {
            yield return WindowRow.Of(
                Path.GetFileNameWithoutExtension(path),
                WindowCell.Button(UiText.Get("corsixnet.replays.watch", "Watch"), () =>
                {
                    host.Close(this);
                    watch(path);
                }));
        }
    }
}
