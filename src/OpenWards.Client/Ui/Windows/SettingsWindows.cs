using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenWards.Content.Saves;
using OpenWards.Content.Settings;

namespace OpenWards.Client.Ui.Windows;

/// <summary>
/// The save and load windows (CorsixTH's <c>dialogs/resizables/file_browsers/save_game.lua</c> and
/// <c>load_game.lua</c>), from the menu (Esc): the saved games in the saves folder, newest first,
/// with the quick save and the autosaves; saving to a new file or over one, and loading one.
/// </summary>
/// <param name="originalSaves">
/// The original game's SAVE folder: when loading, its saves are listed too, to be imported (<c>MAXWELL.H1</c> and so on).
/// </param>
internal sealed class SaveLoadWindow(SaveGameStore store, bool saving, Func<string, bool> save, Func<string, bool> load, string? originalSaves = null) : UiWindow, ITextInputWindow
{
    /// <summary>A window CorsixTH added to the game, drawn in its own look.</summary>
    public override bool UsesResizableLook => true;

    /// <summary>The longest name typed for a new save.</summary>
    public const int MaxNameLength = 40;

    private bool _typing;
    private string _name = string.Empty;

    /// <summary>The save's name being typed (save_game.lua's new savegame box), while it waits for keys.</summary>
    public string TypedName => _name;

    public bool IsCapturing => _typing;

    /// <summary>
    /// The new savegame box's keys: Backspace takes the last character away; Return saves under the name, asking first
    /// if a save of that name exists; Escape stops typing. The characters come as text (<see cref="Type"/>).
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
        }
    }

    /// <summary>
    /// A character typed in the new savegame box, as the keyboard layout gives it: letters of any alphabet (Cyrillic
    /// too), digits, spaces and the punctuation a file name may have.
    /// </summary>
    public void Type(char character)
    {
        if (_typing && _name.Length < MaxNameLength && (char.IsLetterOrDigit(character) || character is ' ' or '-' or '_' or '.' or '(' or ')' or '+')
            && Array.IndexOf(Path.GetInvalidFileNameChars(), character) < 0)
        {
            _name += character;
        }
    }

    private readonly FileBrowserScreen _browser = new();

    private string? _selected;

    /// <summary>CorsixTH's file browser (<see cref="FileBrowserScreen"/>) where its sprites are there; else the table below.</summary>
    public override bool IsFullscreen(IWindowHost host) => ResizablePanels.IsAvailable(host);

    public override Point FullscreenSize => FileBrowserScreen.Size;

    public override Vector2? FullscreenPosition => new(0.5f, 0.25f);

    private List<DialogPanel> BrowserPanels(IWindowHost host)
    {
        var title = saving
            ? UiText.Format("save_game_window.caption", "Save Game (%1%)", Extension)
            : UiText.Format("load_game_window.caption", "Load Game (%1%)", Extension);
        var ok = saving ? UiText.Get("save_game_window.save_button", "Save") : UiText.Get("load_game_window.load_button", "Load");
        return _browser.Panels(title, saving, _name, _typing, ok, () => host.Close(this), () => Confirm(host), () => _typing = !_typing);
    }

    /// <summary>
    /// OK (<c>confirmName</c> when saving, else the selected save): saving under the typed name, which must not be empty
    /// (<c>missing_filename</c>), or the save picked.
    /// </summary>
    private void Confirm(IWindowHost host)
    {
        if (saving && _name.Trim().Length > 0)
        {
            _typing = false;
            TrySave(host, Path.Combine(store.Directory, _name.Trim() + Extension));
        }
        else if (_selected is { } path)
        {
            Choose(host, path);
        }
        else if (saving)
        {
            host.Open(new InformationWindow([UiText.Get("save_game_window.missing_filename", "Please enter the name of the new save or select the save to overwrite.")]));
        }
    }

    /// <summary><c>choiceMade</c>: saving over the save picked (asking first), or loading it.</summary>
    private void Choose(IWindowHost host, string path)
    {
        if (saving)
        {
            TrySave(host, path);
        }
        else if (load(path))
        {
            host.Close(this);
        }
    }

    /// <summary><c>trySave</c>: an existing save is overwritten after <c>confirmation.overwrite_save</c>.</summary>
    private void TrySave(IWindowHost host, string path)
    {
        if (File.Exists(path))
        {
            host.Open(new ConfirmWindow(UiText.Get("confirmation.overwrite_save", "A game has already been saved there. Are you sure you want to overwrite it?"), () =>
            {
                if (save(path))
                {
                    host.Close(this);
                }
            }));
        }
        else if (save(path))
        {
            host.Close(this);
        }
    }

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse) =>
        _browser.Draw(batch, host, origin, mouse, BrowserPanels(host), _browser.Sorted([.. Games()]), saving, _selected);

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous)
    {
        var games = _browser.Sorted([.. Games()]);
        if (saving && _browser.RowAt(local, games.Count, saving) is { } hovered && !_typing)
        {
            // setValueChangeCallback: the save under the mouse names the new savegame box.
            _name = Path.GetFileNameWithoutExtension(games[hovered].Path);
        }

        if (_browser.Mouse(host, local, mouse, previous, BrowserPanels(host), games.Count, saving) is { } row)
        {
            host.PlaySound(WindowLayer.DefaultButtonSound);
            _selected = games[row].Path;
            Choose(host, games[row].Path);
        }

        if (saving && _pendingSave is { } typed)
        {
            _pendingSave = null;
            TrySave(host, Path.Combine(store.Directory, typed));
        }
    }

    public override string? FullscreenTooltip(IWindowHost host, Point local) => ResizablePanels.Tooltip(BrowserPanels(host), local);

    public override Point? FindFullscreenButton(IWindowHost host, string text, int nth) => ResizablePanels.FindButton(BrowserPanels(host), text, nth);

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
            var name = UiText.Format("openwards.save_game.file_name", "Year %1% month %2% day %3%", date.Year, date.Month, date.Day) + Extension;
            yield return WindowRow.Of(WindowCell.Button(UiText.Format("openwards.save_game.save_as", "Save as \"%1%\"", name), () =>
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
            yield return WindowCell.Line(UiText.Get("openwards.save_game.none", "No saved games yet."), dim: true);
            yield break;
        }

        yield return WindowRow.Of(UiText.Get("openwards.save_game.name", "Saved game"), UiText.Get("openwards.save_game.saved_at", "Saved at"), string.Empty);
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
                action = WindowCell.Button(UiText.Get("openwards.save_game.confirm_overwrite", "Sure? Overwrite"), () =>
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
                action = WindowCell.Button(UiText.Get("openwards.save_game.overwrite", "Overwrite"), () => _confirmOverwrite = path, enabled: path != store.QuickSavePath);
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
            found.Add((store.QuickSavePath, UiText.Get("openwards.save_game.quick_save", "Quick save")));
        }

        var autosaves = Path.Combine(store.Directory, SaveGameStore.AutosaveFolder);
        if (Directory.Exists(autosaves))
        {
            found.AddRange(Directory.EnumerateFiles(autosaves, "*" + Extension).Select(path => (path, Path.GetFileNameWithoutExtension(path))));
        }

        if (!saving && originalSaves is not null && Directory.Exists(originalSaves))
        {
            found.AddRange(Directory.EnumerateFiles(originalSaves, "*.H*")
                .Where(path => new FileInfo(path).Length == OpenWards.Formats.OriginalSaveGame.Length)
                .Select(path => (path, UiText.Format("openwards.save_game.original", "Theme Hospital: %1%", Path.GetFileName(path)))));
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
/// <param name="openDialog">
/// Opens one of the settings dialogs (graphics, customise, sound, accessibility): with it and CorsixTH's sprites the
/// window is CorsixTH's <c>UIOptions</c>, whose changes apply at once; without, the table below with Apply.
/// </param>
internal sealed class OptionsWindow(GameConfig current, Action<GameConfig> apply, IReadOnlyList<string>? languages = null, Action? hotkeys = null, Func<string, LanguageFontChoices?>? fonts = null, Action<GameConfig>? preview = null, Action<SettingsDialogKind>? openDialog = null, Action? folders = null) : UiWindow
{
    /// <summary>A window CorsixTH added to the game, drawn in its own look.</summary>
    public override bool UsesResizableLook => true;

    private readonly IReadOnlyList<string> _languages = languages ?? [];

    private readonly GameConfig _current = current;
    private GameConfig _draft = current;

    /// <summary>Whether the game shows a language or font other than the one the window opened with.</summary>
    private bool _previewing;

    private bool _applied;

    public override string Title => UiText.Get("options_window.caption", "Options");

    private readonly ResizablePanels _panels = new();

    private IWindowHost? _host;

    public override bool IsFullscreen(IWindowHost host) => openDialog is not null && ResizablePanels.IsAvailable(host);

    public override Point FullscreenSize => Look().Size;

    public override Vector2? FullscreenPosition => new(0.5f, 0.25f);

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        _host = host;
        var (size, panels) = Look();
        _panels.Draw(batch, host, origin, size, panels, mouse);
    }

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous)
    {
        _host = host;
        _panels.Mouse(host, Look().Panels, local, mouse, previous);
    }

    public override string? FullscreenTooltip(IWindowHost host, Point local) => ResizablePanels.Tooltip(Look().Panels, local);

    public override Point? FindFullscreenButton(IWindowHost host, string text, int nth) => ResizablePanels.FindButton(Look().Panels, text, nth);

    /// <summary>
    /// <c>UIOptions</c>: the language and the autosave frequency, which apply at once (where CorsixTH drops down a list,
    /// the button goes to the next one, the right button to the one before), and under them the buttons to the graphics,
    /// customise, hotkey, sound and accessibility dialogs and Back, two a row (<c>nav_buttons</c>).
    /// </summary>
    private (Point Size, List<DialogPanel> Panels) Look()
    {
        string Text(string name, string fallback) => UiText.Get("options_window." + name, fallback);
        string? Tip(string name) => UiText.Current?.Named("tooltip.options_window." + name);
        var autosave = _draft.AutosaveFrequency switch
        {
            0 => Text("option_off", "Off"),
            1 => UiText.Get("autosave_frequency.monthly", "Monthly"),
            2 => UiText.Get("autosave_frequency.weekly", "Weekly"),
            _ => UiText.Get("autosave_frequency.daily", "Daily"),
        };
        List<DialogSetting> settings =
        [
            // options.lua puts check_for_updates first.
            new(Text("check_for_updates", "Check for Updates"), _draft.CheckForUpdates ? Text("option_on", "On") : Text("option_off", "Off"),
                _ => ApplyNow(() => _draft = _draft with { CheckForUpdates = !_draft.CheckForUpdates }), Tip("check_for_updates"), On: _draft.CheckForUpdates),
            new(Text("language", "Game Language"), _draft.Language, step => ApplyNow(() => StepLanguage(step)), Tip("language")),
            new(Text("autosave_frequency", "Autosave Frequency"), autosave, step => ApplyNow(() => _draft = _draft with { AutosaveFrequency = (_draft.AutosaveFrequency + step + 4) % 4 }), Tip("autosave_frequency")),
        ];
        DialogNavButton Dialog(string name, string fallback, SettingsDialogKind kind) => new(Text(name, fallback), () =>
        {
            _host?.Close(this);
            openDialog?.Invoke(kind);
        }, Tip(name));
        List<DialogNavButton> navigation =
        [
            Dialog("graphics", "Graphics", SettingsDialogKind.Graphics),
            Dialog("customise", "Customise", SettingsDialogKind.Customise),
            .. hotkeys is null ? Array.Empty<DialogNavButton>() : [new DialogNavButton(Text("hotkey", "Hotkeys"), () =>
            {
                _host?.Close(this);
                hotkeys();
            }, Tip("hotkey"))],
            Dialog("sound", "Sound", SettingsDialogKind.Sound),
            Dialog("accessibility", "Accessibility", SettingsDialogKind.Accessibility),
            .. folders is null ? Array.Empty<DialogNavButton>() : [new DialogNavButton(Text("folder", "Folders"), () =>
            {
                _host?.Close(this);
                folders();
            }, Tip("folder"))],
            new(Text("back", "Back"), () => _host?.Close(this), Tip("back")),
        ];
        return SettingsDialogLayout.Build(Title, settings, navigation, extraHeight: 250);
    }

    /// <summary>A change in CorsixTH's look: used and saved at once, as <c>UIResizable:_buttonPressed</c> does.</summary>
    private void ApplyNow(Action change)
    {
        change();
        _applied = true;
        apply(_draft);
    }

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        yield return Switch(UiText.Get("openwards.options.sound_effects", "Sound effects"), _draft.PlaySounds, on => _draft = _draft with { PlaySounds = on });
        yield return Stepper(UiText.Get("audio_window.sound_volume", "Sound volume"), Percent(_draft.SoundVolume), () => _draft = _draft with { SoundVolume = Math.Max(0m, _draft.SoundVolume - 0.1m) }, () => _draft = _draft with { SoundVolume = Math.Min(1m, _draft.SoundVolume + 0.1m) });
        yield return Switch(UiText.Get("accessibility_window.enable_screen_shake", "Screen shake"), _draft.EnableScreenShake, on => _draft = _draft with { EnableScreenShake = on });
        yield return Switch(UiText.Get("openwards.options.adviser", "Adviser"), !_draft.AdviserDisabled, on => _draft = _draft with { AdviserDisabled = !on });
        yield return Switch(UiText.Get("openwards.options.room_information", "Room information"), _draft.RoomInformationDialogs, on => _draft = _draft with { RoomInformationDialogs = on });
        yield return Switch(UiText.Get("openwards.options.edge_scrolling", "Edge scrolling"), !_draft.PreventEdgeScrolling, on => _draft = _draft with { PreventEdgeScrolling = !on });
        yield return Stepper(UiText.Get("accessibility_window.scroll_speed", "Scroll speed"), _draft.ScrollSpeed.ToString(CultureInfo.InvariantCulture), () => _draft = _draft with { ScrollSpeed = Math.Max(1, _draft.ScrollSpeed - 1) }, () => _draft = _draft with { ScrollSpeed = Math.Min(10, _draft.ScrollSpeed + 1) });
        yield return Stepper(UiText.Get("accessibility_window.shift_scroll_speed", "Shift scroll speed"), _draft.ShiftScrollSpeed.ToString(CultureInfo.InvariantCulture), () => _draft = _draft with { ShiftScrollSpeed = Math.Max(1, _draft.ShiftScrollSpeed - 1) }, () => _draft = _draft with { ShiftScrollSpeed = Math.Min(10, _draft.ShiftScrollSpeed + 1) });
        yield return Stepper(UiText.Get("options_window.autosave_frequency", "Autosave"), _draft.AutosaveFrequency switch { 0 => UiText.Get("options_window.option_off", "off"), 1 => UiText.Get("autosave_frequency.monthly", "monthly"), 2 => UiText.Get("autosave_frequency.weekly", "weekly"), _ => UiText.Get("autosave_frequency.daily", "daily") }, () => _draft = _draft with { AutosaveFrequency = Math.Max(0, _draft.AutosaveFrequency - 1) }, () => _draft = _draft with { AutosaveFrequency = Math.Min(3, _draft.AutosaveFrequency + 1) });
        // customise.lua's switches for the settings this game has.
        yield return Switch(UiText.Get("customise_window.remove_destroyed_rooms", "Remove destroyed rooms"), _draft.RemoveDestroyedRooms, on => _draft = _draft with { RemoveDestroyedRooms = on }, UiText.Get("tooltip.customise_window.remove_destroyed_rooms", "If you would like to be able to remove destroyed rooms for a fee, turn this option on"));
        yield return Switch(UiText.Get("customise_window.enable_avg_contents", "Average Contents"), _draft.EnableAverageContents, on => _draft = _draft with { EnableAverageContents = on }, UiText.Get("tooltip.customise_window.enable_avg_contents", "If you would like the game to remember which extra objects you usually add while building rooms, turn this option on"));
        yield return Switch(UiText.Get("customise_window.enable_insurance_blue_banknote", "Insurance Blue Banknote"), _draft.EnableInsuranceBlueBanknote, on => _draft = _draft with { EnableInsuranceBlueBanknote = on }, UiText.Get("tooltip.customise_window.enable_insurance_blue_banknote", "Patient payments made via an insurance company feature a blue floating banknote animation. If you prefer the original display style, where all payments are in green, you can disable this option"));
        yield return Switch(UiText.Get("options_window.check_for_updates", "Check for Updates"), _draft.CheckForUpdates, on => _draft = _draft with { CheckForUpdates = on }, UiText.Get("tooltip.options_window.check_for_updates", "Sets whether the game should look for updates on launch."));
        // sound_setting.lua's announcement volume and accessibility.lua's subtitles, with the announcements' own switch.
        yield return Switch(UiText.Get("openwards.options.announcements", "Announcements"), _draft.PlayAnnouncements, on => _draft = _draft with { PlayAnnouncements = on });
        yield return Stepper(UiText.Get("audio_window.announcement_volume", "Announcement volume"), Percent(_draft.AnnouncementVolume), () => _draft = _draft with { AnnouncementVolume = Math.Max(0m, _draft.AnnouncementVolume - 0.1m) }, () => _draft = _draft with { AnnouncementVolume = Math.Min(1m, _draft.AnnouncementVolume + 0.1m) });
        yield return Switch(UiText.Get("openwards.options.music", "Music"), _draft.PlayMusic, on => _draft = _draft with { PlayMusic = on });
        yield return Stepper(UiText.Get("audio_window.music_volume", "Music volume"), Percent(_draft.MusicVolume), () => _draft = _draft with { MusicVolume = Math.Max(0m, _draft.MusicVolume - 0.1m) }, () => _draft = _draft with { MusicVolume = Math.Min(1m, _draft.MusicVolume + 0.1m) });
        yield return Switch(UiText.Get("accessibility_window.enable_announcer_subtitles", "Announcer subtitles"), _draft.EnableAnnouncerSubtitles, on => _draft = _draft with { EnableAnnouncerSubtitles = on }, UiText.Get("tooltip.accessibility_window.enable_announcer_subtitles", "If you would like the game to display subtitles for your hospital's announcements, turn this option on"));
        var size = string.Create(CultureInfo.InvariantCulture, $"{_draft.Width} x {_draft.Height}");
        yield return WindowRow.Of(UiText.Get("openwards.options.screen", "Screen"), _draft.Fullscreen ? UiText.Format("openwards.options.screen_fullscreen", "%1%, fullscreen", size) : size);
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
            var assign = WindowCell.Button(UiText.Get("openwards.options.assign", "Assign"), () =>
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
                LanguageFontChoices.GameFont => UiText.Get("openwards.options.game_font", LanguageFontChoices.GameFont),
                LanguageFontChoices.SystemFont => UiText.Get("openwards.options.system_font", LanguageFontChoices.SystemFont),
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
    public override string Title => UiText.Get("openwards.menu.caption", "Menu");

    public override string? Hotkey => "Esc";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        yield return WindowRow.Of(
            UiText.Get("openwards.menu.file", "File"),
            Item(UiText.Get("openwards.menu.new_game", "New game"), "newgame"),
            Item(UiText.Get("openwards.menu.save", "Save"), "save"),
            Item(UiText.Get("openwards.menu.load", "Load"), "load"),
            Item(UiText.Get("openwards.menu.quick_save", "Quick save"), "quicksave"),
            Item(UiText.Get("openwards.menu.quick_load", "Quick load"), "quickload"),
            Item(UiText.Get("openwards.menu.restart", "Restart"), "restart"),
            Item(UiText.Get("openwards.menu.quit", "Quit"), "quit"));
        yield return WindowRow.Of(UiText.Get("openwards.menu.replays", "Replays"), Item(UiText.Get("openwards.menu.save_replay", "Save replay"), "savereplay"), Item(UiText.Get("openwards.menu.watch_replay", "Watch replay"), "replays"));
        yield return WindowRow.Of(UiText.Get("openwards.menu.options", "Options"), Item(UiText.Get("openwards.menu.settings", "Settings"), "options"), Item(UiText.Get("openwards.menu.levels", "Levels"), "levels"));
        yield return WindowRow.Of(
            UiText.Get("openwards.menu.charts", "Charts"),
            WindowCell.Button(UiText.Get("openwards.menu.bank", "Bank"), () => host.Open(new BankManagerWindow())),
            WindowCell.Button(UiText.Get("openwards.menu.statement", "Statement"), () => host.Open(new BankStatementWindow())),
            WindowCell.Button(UiText.Get("openwards.menu.staff", "Staff"), () => host.Open(new StaffManagementWindow())),
            WindowCell.Button(UiText.Get("openwards.menu.town_map", "Town map"), () => host.Open(new TownMapWindow())),
            WindowCell.Button(UiText.Get("openwards.menu.casebook", "Casebook"), () => host.Open(new CasebookWindow())),
            WindowCell.Button(UiText.Get("openwards.menu.research", "Research"), () => host.Open(new ResearchWindow())));
        yield return WindowRow.Of(
            string.Empty,
            WindowCell.Button(UiText.Get("openwards.menu.status", "Status"), () => host.Open(new ProgressReportWindow())),
            WindowCell.Button(UiText.Get("openwards.menu.graphs", "Graphs"), () => host.Open(new GraphWindow())),
            WindowCell.Button(UiText.Get("openwards.menu.policy", "Policy"), () => host.Open(new PolicyWindow())),
            WindowCell.Button(UiText.Get("openwards.menu.annual_report", "Annual report"), () => host.Open(new AnnualReportWindow())));
        yield return WindowRow.Of(UiText.Get("openwards.menu.debug", "Debug"), WindowCell.Button(UiText.Get("openwards.menu.cheats", "Cheats"), () => host.Open(new CheatsWindow())));

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
    public override string Title => UiText.Get("openwards.menu.replays", "Replays");

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
            yield return WindowCell.Line(UiText.Get("openwards.replays.none", "No replays yet: Save replay in the menu keeps the game played so far."), dim: true);
            yield break;
        }

        foreach (var path in files)
        {
            yield return WindowRow.Of(
                Path.GetFileNameWithoutExtension(path),
                WindowCell.Button(UiText.Get("openwards.replays.watch", "Watch"), () =>
                {
                    host.Close(this);
                    watch(path);
                }));
        }
    }
}
