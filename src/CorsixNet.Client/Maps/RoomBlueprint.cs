// Blueprint floor tiles and wall animations ported from CorsixTH CorsixTH/Src/th_lua_map.cpp
// (l_map_updateblueprint) and CorsixTH/Lua/dialogs/edit_room.lua (_enterDoorPhase, setDoorBlueprint,
// setWindowBlueprint).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;

namespace CorsixNet.Client.Maps;

/// <summary>
/// What a room blueprint looks like on the map, as in the original game: blue floor tiles and a
/// blue wireframe wall around them, with the door and windows drawn into that wall and marked on
/// the floor. Parts that cannot be built are red.
/// </summary>
internal sealed class RoomBlueprint
{
    /// <summary>Block sprites (VBLK-0) for the floor.</summary>
    public const ushort FloorGood = 24;
    public const ushort FloorGoodCenter = 37;
    public const ushort FloorBad = 67;

    /// <summary>Animations (VSPR-0) for the wall.</summary>
    public const int WallAnimation = 120;
    public const int TopCornerAnimation = 124;
    public const int DoorAnimation = 126;
    public const int WindowAnimation = 130;

    private readonly Dictionary<TilePosition, FloorMark> _floor = [];
    private readonly Dictionary<TilePosition, WallPiece> _walls = [];
    private readonly TileRect _area;

    private RoomBlueprint(TileRect area)
    {
        _area = area;
    }

    /// <summary>A floor tile of the blueprint: a block sprite, drawn half transparent while the blueprint is being dragged.</summary>
    public readonly record struct FloorMark(ushort Sprite, bool Translucent);

    /// <summary>
    /// A piece of the blueprint wall, drawn with the tile it is attached to. West pieces are the
    /// north animation mirrored. Invalid pieces are drawn red.
    /// </summary>
    public readonly record struct WallPiece(int Animation, bool West, bool Invalid, int OffsetX, int OffsetY);

    public TileRect Area => _area;

    public FloorMark? FloorAt(TilePosition tile) => _floor.TryGetValue(tile, out var mark) ? mark : null;

    public WallPiece? WallAt(TilePosition tile) => _walls.TryGetValue(tile, out var piece) ? piece : null;

    /// <summary>
    /// The blueprint while it is being dragged (<paramref name="placed"/> false: translucent floor
    /// with a centre mark) or once the door is being placed (opaque floor).
    /// </summary>
    /// <param name="area">The blueprint's tiles.</param>
    /// <param name="isGood">Whether a tile can be part of the room.</param>
    /// <param name="placed">The blueprint is fixed and the door or windows are being placed.</param>
    public static RoomBlueprint Create(TileRect area, Func<TilePosition, bool> isGood, bool placed)
    {
        var blueprint = new RoomBlueprint(area);
        for (var y = area.Y; y < area.Bottom; y++)
        {
            for (var x = area.X; x < area.Right; x++)
            {
                var tile = new TilePosition(x, y);
                blueprint._floor[tile] = placed
                    ? new FloorMark(FloorGood, Translucent: false)
                    : new FloorMark(isGood(tile) ? FloorGood : FloorBad, Translucent: true);
            }
        }

        if (!placed && area.Width >= 2 && area.Height >= 2)
        {
            // Four tiles in the middle show a mark; CorsixTH picks the top-left middle tile like this.
            var centerX = area.X + ((area.Width - 2) / 2);
            var centerY = area.Y + ((area.Height - 2) / 2);
            blueprint.MarkCenter(new(centerX, centerY), FloorGoodCenter + 2);
            blueprint.MarkCenter(new(centerX + 1, centerY), FloorGoodCenter + 1);
            blueprint.MarkCenter(new(centerX, centerY + 1), FloorGoodCenter + 0);
            blueprint.MarkCenter(new(centerX + 1, centerY + 1), FloorGoodCenter + 3);
        }

        // The top corner carries both its walls; north and south walls run along x, west and east along y.
        // Walls on the south and east sides are attached to the tiles just outside, one pixel up.
        bool Bad(int x, int y) => !placed && !isGood(new TilePosition(x, y));
        blueprint._walls[new(area.X, area.Y)] = new WallPiece(TopCornerAnimation, false, Bad(area.X, area.Y), 0, 0);
        for (var x = area.X; x < area.Right; x++)
        {
            if (x != area.X)
            {
                blueprint._walls[new(x, area.Y)] = new WallPiece(WallAnimation, false, Bad(x, area.Y), 0, 0);
            }

            blueprint._walls[new(x, area.Bottom)] = new WallPiece(WallAnimation, false, Bad(x, area.Bottom - 1), 0, -1);
        }

        for (var y = area.Y; y < area.Bottom; y++)
        {
            if (y != area.Y)
            {
                blueprint._walls[new(area.X, y)] = new WallPiece(WallAnimation, true, Bad(area.X, y), 2, 0);
            }

            blueprint._walls[new(area.Right, y)] = new WallPiece(WallAnimation, true, Bad(area.Right - 1, y), 2, -1);
        }

        return blueprint;
    }

    /// <summary>Draws a door into the wall at <paramref name="door"/>, red if it cannot go there, and marks the floor inside it.</summary>
    public void SetDoor(DoorPlacement door, bool valid)
    {
        if (SetWall(door, DoorAnimation, valid))
        {
            var (inside, side) = InsideOf(door);
            _floor[inside] = new FloorMark(side switch { Side.North => 26, Side.East => 27, Side.South => 30, _ => 31 }, Translucent: false);
        }
    }

    /// <summary>Draws a window into the wall at <paramref name="window"/>, red if it cannot go there; a good one is marked on the floor.</summary>
    public void SetWindow(DoorPlacement window, bool valid)
    {
        if (SetWall(window, WindowAnimation, valid) && valid)
        {
            var (inside, side) = InsideOf(window);
            _floor[inside] = new FloorMark(side switch { Side.North => 33, Side.East => 34, Side.South => 35, _ => 36 }, Translucent: false);
        }
    }

    private void MarkCenter(TilePosition tile, int sprite)
    {
        if (_floor.TryGetValue(tile, out var mark) && mark.Sprite == FloorGood)
        {
            _floor[tile] = mark with { Sprite = (ushort)sprite };
        }
    }

    private bool SetWall(DoorPlacement edge, int animation, bool valid)
    {
        if (!_walls.TryGetValue(edge.Tile, out var piece) || piece.Animation == TopCornerAnimation
            || piece.West != (edge.Edge == EdgeOrientation.West))
        {
            return false;
        }

        _walls[edge.Tile] = piece with { Animation = animation, Invalid = !valid };
        return true;
    }

    /// <summary>The room tile next to a perimeter edge, and which side of the room the edge is on.</summary>
    private (TilePosition Inside, Side Side) InsideOf(DoorPlacement edge)
    {
        var tile = edge.Tile;
        return edge.Edge == EdgeOrientation.North
            ? tile.Y == _area.Y ? (tile, Side.North) : (new TilePosition(tile.X, tile.Y - 1), Side.South)
            : tile.X == _area.X ? (tile, Side.West) : (new TilePosition(tile.X - 1, tile.Y), Side.East);
    }

    private enum Side
    {
        North,
        East,
        South,
        West,
    }
}
