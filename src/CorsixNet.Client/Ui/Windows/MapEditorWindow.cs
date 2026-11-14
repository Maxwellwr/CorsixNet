// The map editor's window, ported from CorsixTH CorsixTH/Lua/dialogs/resizables/map_editor.lua (UIMapEditor: the page
// select buttons, the text pages, pageClicked, buildSpriteButtons, blockClicked). Texts are CorsixTH's
// (languages/english.lua: map_editor_window).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Client.Maps;
using CorsixNet.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// <c>UIMapEditor</c>: the sprite pages (a click on a page shows its sprite buttons below, a click on a sprite button
/// puts it on the cursor), the text pages (paste, delete wall, the players' cameras and heliports, parcels 0 to 9) and
/// the editor's own menu items (<c>save</c>, <c>load</c>, <c>restart</c> and <c>quit</c> go to <paramref name="action"/>).
/// </summary>
internal sealed class MapEditorWindow(MapEditorTool tool, SpriteAtlas blocks, Action<string> action) : UiWindow, IKeyCaptureWindow
{
    private static readonly Color ButtonColor = new(55, 55, 80);
    private static readonly Color SelectedColor = new(154, 146, 198);

    private int _page = -1;

    /// <summary>The custom parcel box's text (<c>parcel_input</c>: numbers only, two at most, 10 at first).</summary>
    private string _parcelText = "10";

    private bool _typingParcel;

    /// <summary>While the parcel box is being typed in, the game sees no keys.</summary>
    public bool IsCapturing => _typingParcel;

    /// <summary>
    /// The parcel box: digits are typed (two at most), Delete takes the last one off, Return sets the parcel and Escape
    /// leaves the box as it was.
    /// </summary>
    public void Capture(CorsixNet.Content.Settings.Hotkey key)
    {
        switch (key.Key)
        {
            case { Length: 1 } digit when char.IsAsciiDigit(digit[0]):
                if (_parcelText.Length < 2)
                {
                    _parcelText += digit;
                }

                break;
            case "delete":
                _parcelText = _parcelText.Length > 0 ? _parcelText[..^1] : _parcelText;
                break;
            case "return":
                _typingParcel = false;
                if (int.TryParse(_parcelText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parcel))
                {
                    // parcel_num: the parcel mode with the number typed; the numbered buttons are raised.
                    Text(EditorMode.Parcel, parcel);
                }

                break;
            case "escape":
                _typingParcel = false;
                break;
        }
    }

    public override string Title => UiText.Get("main_menu.map_edit", "Map Editor");

    public override bool IsPinned => false;

    /// <summary>Near the top left, as <c>setPosition(0.1, 0.1)</c>.</summary>
    public override Vector2? DefaultPosition => new(0.02f, 0.05f);

    /// <summary>The sprite page shown, or null.</summary>
    public EditorPage? Page => _page >= 0 ? MapEditorPages.All[_page] : null;

    public override Point? CanvasSize => new(
        MapEditorPages.Columns * MapEditorPages.ColumnSize,
        MapEditorPages.All.Max(page => page.Buttons.Count == 0 ? 0 : page.Buttons.Max(button => button.Row + button.Height - 1)) * MapEditorPages.RowSize);

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var pages = MapEditorPages.All;
        for (var i = 0; i < pages.Count; i += 3)
        {
            yield return new WindowRow([.. pages.Skip(i).Take(3).Select((page, j) => PageButton(UiText.Get("map_editor_window.pages." + page.Name.ToLowerInvariant().Replace(" ", "_", StringComparison.Ordinal), page.Name), i + j))]);
        }

        var players = tool.Editor.Map.PlayerCount;

        // The editor menu's player count (menu.lua, map:setPlayerCount).
        yield return new WindowRow([.. Enumerable.Range(1, 4).Select(count => WindowCell.Button(
            count == 1 ? UiText.Get("corsixnet.map_editor.one_player", "1 player") : UiText.Format("corsixnet.map_editor.players", "%1% players", count), () => tool.Editor.Map.SetPlayerCount(count), highlight: players == count))]);
        yield return WindowRow.Of(
            WindowCell.Button(UiText.Get("map_editor_window.pages.paste", "Paste"), () => Text(EditorMode.Paste), enabled: tool.Clip is not null, highlight: tool.Mode == EditorMode.Paste),
            WindowCell.Button(UiText.Get("map_editor_window.pages.delete_wall", "Delete wall"), () => Text(EditorMode.DeleteWall), highlight: tool.Mode == EditorMode.DeleteWall));
        yield return new WindowRow([.. Enumerable.Range(0, 4).Select(player => WindowCell.Button(
            UiText.Get("map_editor_window.pages.camera_" + (player + 1), $"Camera {player + 1}"), () => Text(EditorMode.Camera, player), enabled: player < players,
            highlight: tool.Mode == EditorMode.Camera && tool.Number == player))]);
        yield return new WindowRow([.. Enumerable.Range(0, 4).Select(player => WindowCell.Button(
            UiText.Get("map_editor_window.pages.heliport_" + (player + 1), $"Heliport {player + 1}"), () => Text(EditorMode.Heliport, player), enabled: player < players,
            highlight: tool.Mode == EditorMode.Heliport && tool.Number == player))]);
        for (var first = 0; first < 10; first += 5)
        {
            yield return new WindowRow([.. Enumerable.Range(first, 5).Select(parcel => WindowCell.Button(
                UiText.Format("map_editor_window.pages.parcel", "Parcel %d", parcel), () => Text(EditorMode.Parcel, parcel),
                highlight: tool.Mode == EditorMode.Parcel && tool.Number == parcel))]);
        }

        yield return WindowRow.Of(
            WindowCell.Button(_typingParcel ? UiText.Format("corsixnet.map_editor.parcel_typing", "Parcel: %1%_", _parcelText) : UiText.Format("corsixnet.map_editor.set_parcel", "Set parcel: %1%", _parcelText), () => _typingParcel = !_typingParcel,
                highlight: _typingParcel || (tool.Mode == EditorMode.Parcel && tool.Number >= 10))
            with
            { Tooltip = UiText.Get("map_editor_window.pages.set_parcel_tooltip", "Type a parcel number and press Return") });

        yield return WindowRow.Of(
            WindowCell.Button(UiText.Get("corsixnet.map_editor.save_map", "Save map"), () => action("save")) with { Tooltip = UiText.Get("corsixnet.map_editor.save_map_tooltip", "Save the map to the maps folder") },
            WindowCell.Button(UiText.Get("corsixnet.map_editor.load_map", "Load map"), () => action("load")) with { Tooltip = UiText.Get("corsixnet.map_editor.load_map_tooltip", "Load a map from the maps folder") },
            WindowCell.Button(UiText.Get("corsixnet.map_editor.new_map", "New map"), () => action("restart")) with { Tooltip = UiText.Get("corsixnet.map_editor.new_map_tooltip", "Start again from a blank map") },
            WindowCell.Button(UiText.Get("corsixnet.menu.quit", "Quit"), () => action("quit")) with { Tooltip = UiText.Get("corsixnet.map_editor.quit_tooltip", "Back to the main menu") });
        yield return WindowCell.Line(Status(), dim: true);
    }

    /// <summary>A click on the sprite buttons: <c>blockClicked</c>, which toggles the button.</summary>
    public override void CanvasClicked(IWindowHost host, Point local)
    {
        if (Page is not { } page)
        {
            return;
        }

        foreach (var button in page.Buttons)
        {
            if (Bounds(button).Contains(local))
            {
                if (ReferenceEquals(tool.Sprite, button))
                {
                    tool.Choose(EditorMode.None);
                }
                else
                {
                    tool.Choose(EditorMode.Sprite, button);
                }

                return;
            }
        }
    }

    /// <summary><c>buildSpriteButtons</c>' buttons, each drawing its sprites as they lie on the map.</summary>
    public override void DrawCanvas(SpriteBatch batch, Rectangle area, Texture2D pixel, TextRenderer text)
    {
        if (Page is not { } page)
        {
            return;
        }

        foreach (var button in page.Buttons)
        {
            var bounds = Bounds(button);
            bounds.Offset(area.Location);
            batch.Draw(pixel, bounds, ReferenceEquals(tool.Sprite, button) ? SelectedColor : ButtonColor);
            var x = bounds.X + 1 + button.OriginX;
            var y = bounds.Y + 1;
            foreach (var part in button.Parts)
            {
                blocks.Draw(batch, part.Sprite, x + ((part.X - part.Y) * 32), y + ((part.X + part.Y) * 16) - 32, Color.White);
            }
        }
    }

    private static Rectangle Bounds(EditorButton button) => new(
        (button.Column - 1) * MapEditorPages.ColumnSize,
        (button.Row - 1) * MapEditorPages.RowSize,
        (MapEditorPages.ColumnSize * button.Width) - 2,
        (MapEditorPages.RowSize * button.Height) - 2);

    private WindowCell PageButton(string name, int index) =>
        WindowCell.Button(name, () =>
        {
            _page = index;
            tool.Choose(EditorMode.None);
        }, highlight: _page == index);

    /// <summary><c>pageClicked</c> for a text page: no sprite page shows, and the cursor goes to that mode at once.</summary>
    private void Text(EditorMode mode, int number = 0)
    {
        _page = -1;
        tool.Choose(mode, number: number);
    }

    private static string Tile(CorsixNet.Sim.Maps.TilePosition tile) => FormattableString.Invariant($"{tile.X}, {tile.Y}");

    private string Status() => tool.Mode switch
    {
        EditorMode.Sprite => UiText.Get("corsixnet.map_editor.status_sprite", "Left drag paints; right press while dragging paints in blocks; right drag copies."),
        EditorMode.DeleteWall => UiText.Get("corsixnet.map_editor.status_delete_wall", "Left drag removes walls and entrance doors."),
        EditorMode.Parcel => UiText.Format("corsixnet.map_editor.status_parcel", "Left drag puts tiles into parcel %1%.", tool.Number),
        EditorMode.Camera when tool.Number < tool.Editor.Map.PlayerCount => UiText.Format("corsixnet.map_editor.status_camera", "Click the tile where player %1%'s view starts (%2%).", tool.Number + 1, Tile(tool.Editor.Map.GetCameraTile(tool.Number))),
        EditorMode.Heliport when tool.Number < tool.Editor.Map.PlayerCount => UiText.Format("corsixnet.map_editor.status_heliport", "Click the tile where player %1%'s helicopter lands (%2%).", tool.Number + 1, Tile(tool.Editor.Map.GetHeliportTile(tool.Number))),
        EditorMode.Paste => UiText.Get("corsixnet.map_editor.status_paste", "Left click pastes the copied area; a left drag fills an area with it."),
        _ => UiText.Get("corsixnet.map_editor.status_default", "Choose a page; a right drag on the map copies an area."),
    };
}

/// <summary><c>UILoadMap</c>: the maps in the levels folder; a click on one opens it in the editor.</summary>
internal sealed class MapListWindow(IReadOnlyList<string> paths, Action<string> load) : UiWindow
{
    public override string Title => UiText.Get("corsixnet.map_editor.load_map_caption", "Load Map");

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        if (paths.Count == 0)
        {
            yield return WindowCell.Line(UiText.Get("corsixnet.map_editor.no_maps", "No maps found: maps are saved in the levels folder (levels in config.txt)."), dim: true);
        }

        foreach (var path in paths)
        {
            yield return WindowRow.Of(WindowCell.Button(Path.GetFileName(path), () =>
            {
                host.Close(this);
                load(path);
            }));
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(WindowCell.Button(UiText.Get("graphics_window.cancel", "Cancel"), () => host.Close(this)));
    }
}
