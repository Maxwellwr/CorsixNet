// The map editor's cursor on the map, ported from CorsixTH CorsixTH/Lua/dialogs/resizables/map_editor.lua
// (UIMapEditor's cursor states, getDrawPoints, getCursorDragCapabilities, onMouseMove, onMouseDown, onMouseUp,
// drawCursorSpriteAtArea, copyArea, pasteArea).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Content.Levels;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Client.Maps;

/// <summary>What a click on the map does in the editor: the page or button chosen in the editor window.</summary>
internal enum EditorMode
{
    /// <summary>Nothing chosen: the cursor only copies with the right button (<c>disabled</c>).</summary>
    None,

    /// <summary>A sprite button: the left button paints it (<c>grid</c>).</summary>
    Sprite,

    /// <summary>The walls of the tiles go (<c>delete</c>).</summary>
    DeleteWall,

    /// <summary>The tiles go into a parcel (<c>parcel</c>).</summary>
    Parcel,

    /// <summary>A player's camera tile (<c>camera</c>).</summary>
    Camera,

    /// <summary>A player's heliport tile (<c>heliport</c>).</summary>
    Heliport,

    /// <summary>The copied area is pasted (<c>paste</c>).</summary>
    Paste,
}

/// <summary>
/// The map editor's cursor: a press of the left button starts an area that is painted, cleared or pasted when it is
/// let go; the right button marks an area to copy; the right button pressed while the left one drags paints the sprite
/// in blocks the size of the area between the two presses (<c>both</c>). Moving back to where the drag started cancels
/// it.
/// </summary>
internal sealed class MapEditorTool
{
    private TilePosition? _left;
    private TilePosition? _right;
    private bool _both;
    private bool _isDrag;

    public MapEditorTool(MapEditor editor)
    {
        Editor = editor;
    }

    public MapEditor Editor { get; }

    public EditorMode Mode { get; private set; }

    /// <summary>The sprite button chosen, in <see cref="EditorMode.Sprite"/>.</summary>
    public EditorButton? Sprite { get; private set; }

    /// <summary>The parcel, or the player counted from 0, chosen for the parcel, camera and heliport modes.</summary>
    public int Number { get; private set; }

    /// <summary>The copied area, if any.</summary>
    public MapClip? Clip { get; private set; }

    /// <summary>The tile under the cursor, kept on the map.</summary>
    public TilePosition Cursor { get; private set; }

    /// <summary>Whether a button is held down on the map.</summary>
    public bool IsDragging => _left is not null || _right is not null;

    /// <summary>What happens after something changed on the map, such as the parcels' overlay showing.</summary>
    public Action? Changed { get; set; }

    /// <summary><c>pageClicked</c> or <c>blockClicked</c>: another mode; choosing anything but Paste forgets the copied area.</summary>
    public void Choose(EditorMode mode, EditorButton? sprite = null, int number = 0)
    {
        if (mode == EditorMode.Paste && Clip is null)
        {
            mode = EditorMode.None;
        }

        if (mode != EditorMode.Paste)
        {
            Clip = null;
        }

        Mode = mode;
        Sprite = mode == EditorMode.Sprite ? sprite : null;
        Number = number;
        _left = _right = null;
        _both = _isDrag = false;
    }

    /// <summary>Follows the mouse and its buttons for one frame; <paramref name="tile"/> is null off the map.</summary>
    public void Update(TilePosition? tile, bool leftDown, bool leftPressed, bool leftReleased, bool rightPressed, bool rightReleased)
    {
        if (tile is { } hovered)
        {
            Move(Clamp(hovered, 1, 1));
        }

        if (leftPressed && _left is null && _right is null && Mode is not (EditorMode.None or EditorMode.Camera or EditorMode.Heliport))
        {
            _left = Cursor;
            _isDrag = false;
        }
        else if (rightPressed && _left is not null && Mode == EditorMode.Sprite && !_both)
        {
            // Right pressed while dragging a sprite: paint in blocks.
            _both = true;
            _right = Cursor;
        }
        else if (rightPressed && _left is null && _right is null)
        {
            _right = Cursor;
            _isDrag = false;
        }

        if (leftReleased)
        {
            if (Mode is EditorMode.Camera or EditorMode.Heliport && _right is null)
            {
                SetPlayerTile();
            }
            else if (_left is not null)
            {
                Apply();
            }
        }

        if (rightReleased && _right is { } corner && !_both)
        {
            Copy(corner);
        }

        if (!leftDown && _both)
        {
            _left = _right = null;
            _both = false;
        }
    }

    /// <summary><c>getDrawPoints</c>: the tiles where the cursor's red outlines go, with the size of each outline.</summary>
    public IEnumerable<(TilePosition Tile, int SizeX, int SizeY)> Outlines()
    {
        var (sizeX, sizeY) = Mode switch
        {
            EditorMode.Sprite when Sprite is not null && _right is null => (Sprite.SizeX, Sprite.SizeY),
            EditorMode.Paste when Clip is not null && _left is null && _right is null => (Clip.Width, Clip.Height),
            _ => (1, 1),
        };
        foreach (var point in DrawPoints())
        {
            yield return (point, sizeX, sizeY);
        }
    }

    private IEnumerable<TilePosition> DrawPoints()
    {
        if (_both && _left is { } first && _right is { } second)
        {
            // CorsixTH: the right press must be in the area, and the block is the area between the two presses.
            var (minX, maxX) = (Math.Min(first.X, Cursor.X), Math.Max(first.X, Cursor.X));
            var (minY, maxY) = (Math.Min(first.Y, Cursor.Y), Math.Max(first.Y, Cursor.Y));
            var stepX = Math.Abs(first.X - second.X) + 1;
            var stepY = Math.Abs(first.Y - second.Y) + 1;
            if ((minX != maxX || minY != maxY) && second.X >= minX && second.X <= maxX && second.Y >= minY && second.Y <= maxY && (stepX > 1 || stepY > 1))
            {
                return Points(minX, minY, maxX, maxY, stepX, stepY);
            }

            return [];
        }

        var start = _left ?? _right;
        if (start is not { } corner)
        {
            return Mode == EditorMode.None ? [] : [Cursor];
        }

        var x1 = Math.Min(corner.X, Cursor.X);
        var x2 = Math.Max(corner.X, Cursor.X);
        var y1 = Math.Min(corner.Y, Cursor.Y);
        var y2 = Math.Max(corner.Y, Cursor.Y);
        if (x1 == x2 && y1 == y2 && _isDrag)
        {
            // Back where the drag started: cancelled.
            return [];
        }

        var step = _left is not null && Mode == EditorMode.Sprite && Sprite is not null ? (Sprite.SizeX, Sprite.SizeY) : (1, 1);
        return Points(x1, y1, x2, y2, step.Item1, step.Item2);
    }

    private static IEnumerable<TilePosition> Points(int minX, int minY, int maxX, int maxY, int stepX, int stepY)
    {
        for (var x = minX; x <= maxX; x += stepX)
        {
            for (var y = minY; y <= maxY; y += stepY)
            {
                yield return new TilePosition(x, y);
            }
        }
    }

    /// <summary><c>onMouseMove</c>: while dragging, the sprite's drag capabilities hold the cursor to a line or to the start.</summary>
    private void Move(TilePosition tile)
    {
        if (tile == Cursor)
        {
            return;
        }

        if (_left is { } start && !_both)
        {
            _isDrag = true;
            var (alongX, alongY) = Mode == EditorMode.Sprite && Sprite is not null ? Sprite.Drag : (true, true);
            tile = new TilePosition(alongX ? tile.X : start.X, alongY ? tile.Y : start.Y);
        }
        else if (_right is not null || _left is not null)
        {
            _isDrag = true;
        }

        Cursor = tile;
    }

    /// <summary><c>onMouseUp</c> for the left button: the area is painted, cleared, put into the parcel or pasted.</summary>
    private void Apply()
    {
        var points = DrawPoints().ToList();
        switch (Mode)
        {
            case EditorMode.Sprite when Sprite is not null:
                foreach (var point in points)
                {
                    Paint(Sprite, point);
                }

                break;
            case EditorMode.DeleteWall:
                Editor.DeleteWalls(points);
                break;
            case EditorMode.Parcel:
                Editor.SetParcel(points, Number);
                break;
            case EditorMode.Paste when Clip is not null:
                Paste();
                break;
        }

        _left = null;
        _isDrag = false;
        Changed?.Invoke();
    }

    /// <summary><c>drawCursorSpriteAtArea</c>: the button's sprites and doors, kept on the map.</summary>
    private void Paint(EditorButton button, TilePosition point)
    {
        var at = Clamp(point, button.SizeX, button.SizeY);
        foreach (var part in button.Parts)
        {
            TilePosition[] tile = [new(at.X + part.X - 1, at.Y + part.Y - 1)];
            switch (part.Kind)
            {
                case EditorSpriteKind.North:
                    Editor.PaintNorthWall(tile, part.Sprite);
                    break;
                case EditorSpriteKind.West:
                    Editor.PaintWestWall(tile, part.Sprite);
                    break;
                default:
                    var kind = part.Kind switch
                    {
                        EditorSpriteKind.Hospital => EditorFloorKind.Hospital,
                        EditorSpriteKind.Road => EditorFloorKind.Road,
                        _ => EditorFloorKind.Outside,
                    };
                    Editor.PaintFloor(tile, part.Sprite, kind);
                    break;
            }
        }

        foreach (var door in button.Doors)
        {
            Editor.PlaceEntranceDoor(new TilePosition(at.X + door.X - 1, at.Y + door.Y - 1), door.West);
        }
    }

    /// <summary>
    /// <c>pasteArea</c>: without a drag, the copied area's size from the tile clicked; otherwise the dragged area, filled
    /// with copies.
    /// </summary>
    private void Paste()
    {
        if (_left is not { } start || Clip is null)
        {
            return;
        }

        if (start == Cursor)
        {
            if (_isDrag)
            {
                return;
            }

            var end = Clamp(new TilePosition(Cursor.X + Clip.Width - 1, Cursor.Y + Clip.Height - 1), 1, 1);
            Editor.Paste(Clip, Cursor, end);
            return;
        }

        Editor.Paste(Clip, start, Cursor);
    }

    /// <summary><c>copyArea</c>: the area is copied and the editor goes on to paste it.</summary>
    private void Copy(TilePosition corner)
    {
        _right = null;
        if (corner == Cursor && _isDrag)
        {
            return;
        }

        Clip = Editor.Copy(corner, Cursor);
        Mode = EditorMode.Paste;
        Sprite = null;
        _isDrag = false;
    }

    private void SetPlayerTile()
    {
        if (Number >= Editor.Map.PlayerCount)
        {
            return;
        }

        if (Mode == EditorMode.Camera)
        {
            Editor.SetCamera(Number, Cursor);
        }
        else
        {
            Editor.SetHeliport(Number, Cursor);
        }

        Changed?.Invoke();
    }

    /// <summary><c>areaOnWorld</c>: the base tile moved so that an area of this size stays on the map.</summary>
    private TilePosition Clamp(TilePosition tile, int sizeX, int sizeY) => new(
        Math.Clamp(tile.X, 0, Editor.Map.Width - sizeX),
        Math.Clamp(tile.Y, 0, Editor.Map.Height - sizeY));
}
