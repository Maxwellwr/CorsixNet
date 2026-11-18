// The folders window of the options, ported from CorsixTH CorsixTH/Lua/dialogs/resizables/folder_settings.lua (UIFolder:
// 420 by 240, the caption, a row each for the Theme Hospital data, saves, screenshots and music folders with a button
// showing where it is, X to put one back to its default or clear it, and Back).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenWards.Content.Settings;

namespace OpenWards.Client.Ui.Windows;

/// <summary>
/// <c>UIFolder</c>: each folder's button opens the folder browser (<see cref="DirectoryBrowserWindow"/>); a folder chosen
/// is saved in the settings at once. A new game data folder needs the game started again, as CorsixTH restarts.
/// </summary>
/// <param name="config">The settings as they are now.</param>
/// <param name="apply">Uses and saves new settings.</param>
/// <param name="back">Opens the options again.</param>
/// <param name="defaultSaves">Where saves go when no folder is set (<c>getDefaultSavegameDir</c>).</param>
/// <param name="defaultScreenshots">Where screenshots go when no folder is set (<c>getDefaultScreenshotsDir</c>).</param>
/// <param name="restart">Starts the game again with a new data folder (<c>App:restart</c>), or null.</param>
internal sealed class FolderSettingsWindow(GameConfig config, Action<GameConfig> apply, Action back, string defaultSaves, string defaultScreenshots, Action? restart = null) : UiWindow
{
    public static readonly Point Size = new(420, 240);

    private readonly ResizablePanels _panels = new();
    private GameConfig _config = config;
    private IWindowHost? _host;

    public GameConfig Config => _config;

    public override string Title => UiText.Get("folders_window.caption", "Folder Locations");

    public override bool UsesResizableLook => true;

    public override bool IsSameAs(UiWindow other) => other is FolderSettingsWindow;

    public override Vector2? DefaultPosition => new(0.5f, 0.25f);

    public override Vector2? FullscreenPosition => new(0.5f, 0.25f);

    public override bool IsFullscreen(IWindowHost host) => ResizablePanels.IsAvailable(host);

    public override Point FullscreenSize => Size;

    /// <summary>The rows: the label, where the folder is, its tooltip, what its button does, and what X does (or null).</summary>
    private List<(string Label, string Value, string Tooltip, Action Browse, Action? Reset)> Folders()
    {
        string Tip(string name, string fallback, params object[] args) => UiText.Format("tooltip.folders_window." + name, fallback, args);
        var saves = _config.Savegames is { Length: > 0 } s ? s : defaultSaves;
        var shots = _config.Screenshots is { Length: > 0 } p ? p : defaultScreenshots;
        var notSet = Tip("not_specified", "No folder location specified yet!");
        return
        [
            (UiText.Get("folders_window.data_label", "TH Data"), _config.ThemeHospitalInstall,
                Tip("browse_data", "Browse for another location of a Theme Hospital installation. ( Current location: %1% )", _config.ThemeHospitalInstall),
                () => Browse("new_th_location", "Here you can specify a new Theme Hospital installation directory.", ChooseData), null),
            (UiText.Get("folders_window.savegames_label", "Saves"), saves,
                Tip("browse_saves", "Browse for another location for your Saved Games directory ( Current location: %1% ) ", saves),
                () => Browse("savegames_location", "Select the directory you want to use for Saves", ChooseSaves),
                () => Apply(_config with { Savegames = string.Empty })),
            (UiText.Get("folders_window.screenshots_label", "Screenshots"), shots,
                Tip("browse_screenshots", "Browse for another location for your Screenshots directory ( Current location: %1% ) ", shots),
                () => Browse("screenshots_location", "Select the directory you want to use for Screenshots", path => Apply(_config with { Screenshots = path })),
                () => Apply(_config with { Screenshots = string.Empty })),
            (UiText.Get("folders_window.music_label", "Music"), _config.AudioMusic is { Length: > 0 } music ? music : notSet,
                _config.AudioMusic.Length > 0 ? Tip("browse_music", "Browse for another location for your Music directory ( Current location: %1% ) ", _config.AudioMusic) : notSet,
                () => Browse("music_location", "Select the directory you want to use for your Music.", ChooseMusic),
                () => Apply(_config with { AudioMusic = string.Empty, PlayOwnMusic = false })),
        ];
    }

    private void Browse(string instruction, string fallback, Action<string> chosen) =>
        _host?.Open(new DirectoryBrowserWindow(UiText.Get("folders_window." + instruction, fallback), chosen));

    private void Apply(GameConfig changed)
    {
        _config = changed;
        apply(changed);
    }

    /// <summary><c>buttonBrowseForSavegames</c>: saves go to the folder chosen from now on.</summary>
    internal void ChooseSaves(string path) => Apply(_config with { Savegames = path });

    /// <summary><c>buttonBrowseForTHInstall</c>: a folder with the game's data is saved and the game starts again.</summary>
    internal void ChooseData(string path)
    {
        if (!Formats.ThemeHospitalInstall.TryOpen(path, out _, out var error))
        {
            _host?.Open(new InformationWindow([error]));
            return;
        }

        Apply(_config with { ThemeHospitalInstall = path });
        restart?.Invoke();
    }

    /// <summary><c>buttonBrowseForAudio_music</c>, with <c>App:verifyPlayerMusicDir</c>'s check.</summary>
    internal void ChooseMusic(string path)
    {
        if (SettingsDialog.MusicFolderProblem(path) is { } problem)
        {
            _host?.Open(new InformationWindow([.. problem.Split("//").Select(part => part.Trim()).Where(part => part.Length > 0)]));
            Apply(_config with { AudioMusic = string.Empty, PlayOwnMusic = false });
            return;
        }

        Apply(_config with { AudioMusic = path, PlayOwnMusic = true });
    }

    private List<DialogPanel> Panels(IWindowHost host)
    {
        _host = host;
        List<DialogPanel> panels = [DialogPanel.Title(new Rectangle(110, 10, 200, 20), Title)];
        var y = 50;
        foreach (var (label, value, tooltip, browse, reset) in Folders())
        {
            panels.Add(DialogPanel.Caption(new Rectangle(20, y, 130, 20), label));
            var width = reset is null ? 240 : 220;
            panels.Add(DialogPanel.Button(new Rectangle(160, y, width, 20), value, _ => browse(), tooltip));
            if (reset is not null)
            {
                panels.Add(DialogPanel.Button(new Rectangle(380, y, 20, 20), "X", _ => reset(),
                    UiText.Get("tooltip.folders_window.clear_directory", "Clear current directory selection")));
            }

            y += 25;
        }

        panels.Add(DialogPanel.Button(new Rectangle(50, y + 15, 320, 40), UiText.Get("folders_window.back", "Back"), _ =>
        {
            host.Close(this);
            back();
        }, UiText.Get("tooltip.folders_window.back", "Close this menu and go back to the Settings Menu")));
        return panels;
    }

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse) =>
        _panels.Draw(batch, host, origin, Size, Panels(host), mouse);

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous) =>
        _panels.Mouse(host, Panels(host), local, mouse, previous);

    public override string? FullscreenTooltip(IWindowHost host, Point local) => ResizablePanels.Tooltip(Panels(host), local);

    public override Point? FindFullscreenButton(IWindowHost host, string text, int nth) => ResizablePanels.FindButton(Panels(host), text, nth);

    /// <summary>Without CorsixTH's sprites: each folder with its button and X, then Back.</summary>
    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        _host = host;
        foreach (var (label, value, tooltip, browse, reset) in Folders())
        {
            yield return reset is null
                ? WindowRow.Of(new WindowCell(label), WindowCell.Button(value, browse) with { Tooltip = tooltip })
                : WindowRow.Of(new WindowCell(label), WindowCell.Button(value, browse) with { Tooltip = tooltip }, WindowCell.Button("X", reset));
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(WindowCell.Button(UiText.Get("folders_window.back", "Back"), () =>
        {
            host.Close(this);
            back();
        }));
    }
}
