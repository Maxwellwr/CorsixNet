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
internal sealed class MapEditorWindow(MapEditorTool tool, SpriteAtlas blocks, Action<string> action) : UiWindow
{
    private static readonly Color ButtonColor = new(55, 55, 80);
    private static readonly Color SelectedColor = new(154, 146, 198);

    private int _page = -1;

    public override string Title => "Map Editor";

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
            yield return new WindowRow([.. pages.Skip(i).Take(3).Select((page, j) => PageButton(page.Name, i + j))]);
        }

        var players = tool.Editor.Map.PlayerCount;

        // The editor menu's player count (menu.lua, map:setPlayerCount).
        yield return new WindowRow([.. Enumerable.Range(1, 4).Select(count => WindowCell.Button(
            count == 1 ? "1 player" : $"{count} players", () => tool.Editor.Map.SetPlayerCount(count), highlight: players == count))]);
        yield return WindowRow.Of(
            WindowCell.Button("Paste", () => Text(EditorMode.Paste), enabled: tool.Clip is not null, highlight: tool.Mode == EditorMode.Paste),
            WindowCell.Button("Delete wall", () => Text(EditorMode.DeleteWall), highlight: tool.Mode == EditorMode.DeleteWall));
        yield return new WindowRow([.. Enumerable.Range(0, 4).Select(player => WindowCell.Button(
            $"Camera {player + 1}", () => Text(EditorMode.Camera, player), enabled: player < players,
            highlight: tool.Mode == EditorMode.Camera && tool.Number == player))]);
        yield return new WindowRow([.. Enumerable.Range(0, 4).Select(player => WindowCell.Button(
            $"Heliport {player + 1}", () => Text(EditorMode.Heliport, player), enabled: player < players,
            highlight: tool.Mode == EditorMode.Heliport && tool.Number == player))]);
        for (var first = 0; first < 10; first += 5)
        {
            yield return new WindowRow([.. Enumerable.Range(first, 5).Select(parcel => WindowCell.Button(
                $"Parcel {parcel}", () => Text(EditorMode.Parcel, parcel),
                highlight: tool.Mode == EditorMode.Parcel && tool.Number == parcel))]);
        }

        yield return WindowRow.Of(
            WindowCell.Button("Save map", () => action("save")) with { Tooltip = "Save the map to the maps folder" },
            WindowCell.Button("Load map", () => action("load")) with { Tooltip = "Load a map from the maps folder" },
            WindowCell.Button("New map", () => action("restart")) with { Tooltip = "Start again from a blank map" },
            WindowCell.Button("Quit", () => action("quit")) with { Tooltip = "Back to the main menu" });
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
        EditorMode.Sprite => "Left drag paints; right press while dragging paints in blocks; right drag copies.",
        EditorMode.DeleteWall => "Left drag removes walls and entrance doors.",
        EditorMode.Parcel => $"Left drag puts tiles into parcel {tool.Number}.",
        EditorMode.Camera when tool.Number < tool.Editor.Map.PlayerCount => $"Click the tile where player {tool.Number + 1}'s view starts ({Tile(tool.Editor.Map.GetCameraTile(tool.Number))}).",
        EditorMode.Heliport when tool.Number < tool.Editor.Map.PlayerCount => $"Click the tile where player {tool.Number + 1}'s helicopter lands ({Tile(tool.Editor.Map.GetHeliportTile(tool.Number))}).",
        EditorMode.Paste => "Left click pastes the copied area; a left drag fills an area with it.",
        _ => "Choose a page; a right drag on the map copies an area.",
    };
}

/// <summary><c>UILoadMap</c>: the maps in the levels folder; a click on one opens it in the editor.</summary>
internal sealed class MapListWindow(IReadOnlyList<string> paths, Action<string> load) : UiWindow
{
    public override string Title => "Load Map";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        if (paths.Count == 0)
        {
            yield return WindowCell.Line("No maps found: maps are saved in the levels folder (levels in config.txt).", dim: true);
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
        yield return WindowRow.Of(WindowCell.Button("Cancel", () => host.Close(this)));
    }
}
