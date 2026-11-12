using System.Globalization;
using CorsixNet.Content.Saves;
using CorsixNet.Content.Settings;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The save and load windows (CorsixTH's <c>dialogs/resizables/file_browsers/save_game.lua</c> and
/// <c>load_game.lua</c>), from the menu (Esc): the saved games in the saves folder, newest first,
/// with the quick save and the autosaves; saving to a new file or over one, and loading one.
/// </summary>
internal sealed class SaveLoadWindow(SaveGameStore store, bool saving, Func<string, bool> save, Func<string, bool> load) : UiWindow
{
    public const string Extension = ".sav";

    private string? _confirmOverwrite;

    public override string Title => saving ? "Save game" : "Load game";

    public override bool IsSameAs(UiWindow other) => other is SaveLoadWindow;

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        if (saving)
        {
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

/// <summary>
/// The options window (CorsixTH's <c>dialogs/resizables/options.lua</c> with <c>customise.lua</c> and
/// <c>sound_setting.lua</c>): sound effects and their volume, screen shake, edge scrolling, scroll speeds and the
/// autosave frequency, and the language among the installed ones (CorsixTH's <c>options.lua</c> language list). Changes
/// are kept in a draft until Apply, which saves <c>config.txt</c> and uses them at once; the screen size takes effect on
/// the next start.
/// </summary>
internal sealed class OptionsWindow(GameConfig current, Action<GameConfig> apply, IReadOnlyList<string>? languages = null, Action? hotkeys = null) : UiWindow
{
    private readonly IReadOnlyList<string> _languages = languages ?? [];

    private readonly GameConfig _current = current;
    private GameConfig _draft = current;

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
            ? Stepper("Language", _draft.Language, () => StepLanguage(-1), () => StepLanguage(+1))
            : WindowRow.Of("Language", _draft.Language);
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
                apply(_draft);
                host.Close(this);
            }, _draft != _current),
            WindowCell.Button("Cancel", () => host.Close(this)));
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
