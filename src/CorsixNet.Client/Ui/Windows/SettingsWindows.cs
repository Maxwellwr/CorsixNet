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

    public override string Title => saving ? "Save game" : "Load game";

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
            var name = string.Create(CultureInfo.InvariantCulture, $"Year {date.Year} month {date.Month} day {date.Day}{Extension}");
            yield return WindowRow.Of(WindowCell.Button($"Save as \"{name}\"", () =>
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
            yield return WindowCell.Line("No saved games yet.", dim: true);
            yield break;
        }

        yield return WindowRow.Of("Saved game", "Saved at", string.Empty);
        foreach (var (path, label) in games)
        {
            var when = File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            WindowCell action;
            if (!saving)
            {
                action = WindowCell.Button("Load", () =>
                {
                    if (load(path))
                    {
                        host.Close(this);
                    }
                });
            }
            else if (_confirmOverwrite == path)
            {
                action = WindowCell.Button("Sure? Overwrite", () =>
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
                action = WindowCell.Button("Overwrite", () => _confirmOverwrite = path, enabled: label != "Quick save");
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
            found.Add((store.QuickSavePath, "Quick save"));
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
/// are kept in a draft until Apply, which saves <c>config.txt</c> and uses them at once; the screen size takes effect on
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

    public override string Title => "Options";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        yield return Switch("Sound effects", _draft.PlaySounds, on => _draft = _draft with { PlaySounds = on });
        yield return Stepper("Sound volume", Percent(_draft.SoundVolume), () => _draft = _draft with { SoundVolume = Math.Max(0m, _draft.SoundVolume - 0.1m) }, () => _draft = _draft with { SoundVolume = Math.Min(1m, _draft.SoundVolume + 0.1m) });
        yield return Switch("Screen shake", _draft.EnableScreenShake, on => _draft = _draft with { EnableScreenShake = on });
        yield return Switch("Adviser", !_draft.AdviserDisabled, on => _draft = _draft with { AdviserDisabled = !on });
        yield return Switch("Room information", _draft.RoomInformationDialogs, on => _draft = _draft with { RoomInformationDialogs = on });
        yield return Switch("Edge scrolling", !_draft.PreventEdgeScrolling, on => _draft = _draft with { PreventEdgeScrolling = !on });
        yield return Stepper("Scroll speed", _draft.ScrollSpeed.ToString(CultureInfo.InvariantCulture), () => _draft = _draft with { ScrollSpeed = Math.Max(1, _draft.ScrollSpeed - 1) }, () => _draft = _draft with { ScrollSpeed = Math.Min(10, _draft.ScrollSpeed + 1) });
        yield return Stepper("Shift scroll speed", _draft.ShiftScrollSpeed.ToString(CultureInfo.InvariantCulture), () => _draft = _draft with { ShiftScrollSpeed = Math.Max(1, _draft.ShiftScrollSpeed - 1) }, () => _draft = _draft with { ShiftScrollSpeed = Math.Min(10, _draft.ShiftScrollSpeed + 1) });
        yield return Stepper("Autosave", _draft.AutosaveFrequency switch { 0 => "off", 1 => "monthly", 2 => "weekly", _ => "daily" }, () => _draft = _draft with { AutosaveFrequency = Math.Max(0, _draft.AutosaveFrequency - 1) }, () => _draft = _draft with { AutosaveFrequency = Math.Min(3, _draft.AutosaveFrequency + 1) });
        yield return WindowRow.Of("Screen", string.Create(CultureInfo.InvariantCulture, $"{_draft.Width} x {_draft.Height}{(_draft.Fullscreen ? ", fullscreen" : string.Empty)}"));
        yield return _languages.Count > 1
            ? Stepper("Language", _draft.Language, () => Preview(() => StepLanguage(-1)), () => Preview(() => StepLanguage(+1)))
            : WindowRow.Of("Language", _draft.Language);
        if (fonts?.Invoke(_draft.Language) is { } choices)
        {
            yield return Stepper("Font", FontName(choices), () => Preview(() => StepFont(fonts(_draft.Language)?.Fonts ?? [], -1)), () => Preview(() => StepFont(fonts(_draft.Language)?.Fonts ?? [], +1)));
        }
        if (hotkeys is not null)
        {
            // buttonHotkey: the options close for the hotkey assignment dialog, which opens them again.
            var assign = WindowCell.Button("Assign", () =>
            {
                host.Close(this);
                hotkeys();
            });
            yield return WindowRow.Of("Hotkeys", assign with { Tooltip = "Change keyboard hotkeys." });
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(
            WindowCell.Button("Apply", () =>
            {
                _applied = true;
                apply(_draft);
                host.Close(this);
            }, _draft != _current),
            WindowCell.Button("Cancel", () => host.Close(this)));
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
            return choices.Default;
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

    private static WindowRow Switch(string label, bool on, Action<bool> set) =>
        WindowRow.Of(label, WindowCell.Button("On", () => set(true), !on, on), WindowCell.Button("Off", () => set(false), on, !on));

    private static WindowRow Stepper(string label, string value, Action down, Action up) =>
        WindowRow.Of(label, WindowCell.Button("-", down), WindowCell.Number(value), WindowCell.Button("+", up));
}

/// <summary>
/// The menu (CorsixTH's top menu bar, <c>dialogs/menu.lua</c>, which Esc shows): saving, loading, restarting and
/// quitting, the options, the charts and the debug windows.
/// </summary>
internal sealed class MenuWindow(Action<string> action) : UiWindow
{
    public override string Title => "Menu";

    public override string? Hotkey => "Esc";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        yield return WindowRow.Of(
            "File",
            Item("New game", "newgame"),
            Item("Save", "save"),
            Item("Load", "load"),
            Item("Quick save", "quicksave"),
            Item("Quick load", "quickload"),
            Item("Restart", "restart"),
            Item("Quit", "quit"));
        yield return WindowRow.Of("Replays", Item("Save replay", "savereplay"), Item("Watch replay", "replays"));
        yield return WindowRow.Of("Options", Item("Settings", "options"), Item("Levels", "levels"));
        yield return WindowRow.Of(
            "Charts",
            WindowCell.Button("Bank", () => host.Open(new BankManagerWindow())),
            WindowCell.Button("Statement", () => host.Open(new BankStatementWindow())),
            WindowCell.Button("Staff", () => host.Open(new StaffManagementWindow())),
            WindowCell.Button("Town map", () => host.Open(new TownMapWindow())),
            WindowCell.Button("Casebook", () => host.Open(new CasebookWindow())),
            WindowCell.Button("Research", () => host.Open(new ResearchWindow())));
        yield return WindowRow.Of(
            string.Empty,
            WindowCell.Button("Status", () => host.Open(new ProgressReportWindow())),
            WindowCell.Button("Graphs", () => host.Open(new GraphWindow())),
            WindowCell.Button("Policy", () => host.Open(new PolicyWindow())),
            WindowCell.Button("Annual report", () => host.Open(new AnnualReportWindow())));
        yield return WindowRow.Of("Debug", WindowCell.Button("Cheats", () => host.Open(new CheatsWindow())));

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
    public override string Title => "Replays";

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
            yield return WindowCell.Line("No replays yet: Save replay in the menu keeps the game played so far.", dim: true);
            yield break;
        }

        foreach (var path in files)
        {
            yield return WindowRow.Of(
                Path.GetFileNameWithoutExtension(path),
                WindowCell.Button("Watch", () =>
                {
                    host.Close(this);
                    watch(path);
                }));
        }
    }
}
