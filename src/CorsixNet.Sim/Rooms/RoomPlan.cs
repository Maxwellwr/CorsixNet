// Door and window rules (no corners, how the cursor picks a wall) and room removal ported from
// CorsixTH CorsixTH/Lua/dialogs/edit_room.lua (screenToWall, setWindowBlueprint, removeRoom, _remove_wall_line).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Rooms;

/// <summary>A rectangle of tiles in tile units.</summary>
public readonly record struct TileRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool Contains(TilePosition tile) => tile.X >= X && tile.X < Right && tile.Y >= Y && tile.Y < Bottom;

    /// <summary>The rectangle spanned by two corner tiles, in any order.</summary>
    public static TileRect FromCorners(TilePosition a, TilePosition b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        return new TileRect(x, y, Math.Abs(a.X - b.X) + 1, Math.Abs(a.Y - b.Y) + 1);
    }
}

/// <summary>
/// A tile edge on a room's perimeter, where a door or a window goes. An edge on the north or west
/// side of the room lies on an edge of a tile inside the room; an edge on the south or east side
/// lies on the north or west edge of the tile just outside.
/// </summary>
public readonly record struct DoorPlacement(TilePosition Tile, EdgeOrientation Edge);

/// <summary>A room the player wants to build.</summary>
/// <param name="RoomType">Id of the room's definition.</param>
/// <param name="Area">The room's tiles.</param>
/// <param name="Door">Where the door goes.</param>
/// <param name="Windows">Where windows go, if anywhere.</param>
public sealed record RoomPlan(string RoomType, TileRect Area, DoorPlacement Door, IReadOnlyList<DoorPlacement>? Windows = null)
{
    /// <summary>Most windows a plan may have; more than a room's perimeter could hold anyway.</summary>
    public const int MaxWindows = 256;

    public IReadOnlyList<DoorPlacement> WindowList => Windows ?? [];

    /// <summary>Plans are equal when they build the same room; no windows and an empty window list are the same.</summary>
    public bool Equals(RoomPlan? other)
    {
        return other is not null
            && RoomType == other.RoomType
            && Area == other.Area
            && Door == other.Door
            && WindowList.SequenceEqual(other.WindowList);
    }

    public override int GetHashCode() => (Area.X * 31) + (Area.Y * 17) + WindowList.Count;
}

/// <summary>Why a room plan cannot be built.</summary>
public enum RoomPlanProblem
{
    None,
    UnknownRoomType,
    TooSmall,
    OutsideMap,
    NotBuildable,
    NotOwned,
    OverlapsRoom,
    WallInside,
    DoorNotOnPerimeter,
    DoorInCorner,
    DoorInWall,
    DoorBlocked,
    WindowNotOnPerimeter,
    WindowInCorner,
    WindowInWall,
    WindowOnDoor,
    NoWindows,
    TooManyWindows,
}

/// <summary>
/// Checks room plans and builds, confirms and removes rooms. The client uses the same checks for
/// its preview, so a plan that looks valid is accepted when the command executes on every machine.
/// </summary>
public static class RoomBuilder
{
    /// <summary>Checks the area only, before a door has been chosen.</summary>
    public static RoomPlanProblem CheckArea(LevelMap map, RoomDefinition? definition, TileRect area, int player)
    {
        if (definition is null)
        {
            return RoomPlanProblem.UnknownRoomType;
        }

        if (area.Width < definition.MinimumSize || area.Height < definition.MinimumSize)
        {
            return RoomPlanProblem.TooSmall;
        }

        if (area.X < 0 || area.Y < 0 || area.Right > map.Width || area.Bottom > map.Height)
        {
            return RoomPlanProblem.OutsideMap;
        }

        for (var y = area.Y; y < area.Bottom; y++)
        {
            for (var x = area.X; x < area.Right; x++)
            {
                var problem = CheckTile(map, area, new TilePosition(x, y), player);
                if (problem != RoomPlanProblem.None)
                {
                    return problem;
                }
            }
        }

        return RoomPlanProblem.None;
    }

    /// <summary>
    /// Checks one tile of <paramref name="area"/> (which must lie on the map) for <paramref name="player"/>
    /// (0-based), as CorsixTH marks each blueprint tile good or bad.
    /// </summary>
    public static RoomPlanProblem CheckTile(LevelMap map, TileRect area, TilePosition position, int player)
    {
        var (x, y) = (position.X, position.Y);
        var tile = map[x, y];
        if (tile.Has(TileFlags.Room))
        {
            return RoomPlanProblem.OverlapsRoom;
        }

        if (!tile.Has(TileFlags.Buildable | TileFlags.Hospital))
        {
            return RoomPlanProblem.NotBuildable;
        }

        if (map.GetParcelOwner(tile.Parcel) != player + 1)
        {
            return RoomPlanProblem.NotOwned;
        }

        // Walls between tiles of the area would split the room.
        if ((x > area.X && tile.WestWall != 0) || (y > area.Y && tile.NorthWall != 0))
        {
            return RoomPlanProblem.WallInside;
        }

        return RoomPlanProblem.None;
    }

    /// <summary>Checks a complete plan for <paramref name="player"/> (0-based).</summary>
    public static RoomPlanProblem Check(LevelMap map, GameContent content, RoomPlan plan, int player)
    {
        var definition = content.FindRoom(plan.RoomType);
        var problem = CheckArea(map, definition, plan.Area, player);
        if (problem == RoomPlanProblem.None)
        {
            problem = CheckDoor(map, content, plan.Area, plan.Door, definition?.SwingDoors ?? false);
        }

        if (problem != RoomPlanProblem.None)
        {
            return problem;
        }

        var windows = plan.WindowList;
        if (windows.Count > RoomPlan.MaxWindows)
        {
            return RoomPlanProblem.TooManyWindows;
        }

        for (var i = 0; i < windows.Count; i++)
        {
            problem = CheckWindow(map, definition!, plan.Area, plan.Door, windows[i]);
            if (problem != RoomPlanProblem.None)
            {
                return problem;
            }

            for (var j = 0; j < i; j++)
            {
                if (windows[j] == windows[i])
                {
                    return RoomPlanProblem.WindowInWall;
                }
            }
        }

        return RoomPlanProblem.None;
    }

    /// <summary>Checks a door for an area that passed <see cref="CheckArea"/>.</summary>
    public static RoomPlanProblem CheckDoor(LevelMap map, GameContent content, TileRect area, DoorPlacement door, bool swing = false)
    {
        if (OutsideTile(area, door) is not { } outside)
        {
            return RoomPlanProblem.DoorNotOnPerimeter;
        }

        if (IsCorner(area, door))
        {
            return RoomPlanProblem.DoorInCorner;
        }

        var existing = WallAt(map, door);
        if (existing != 0 && (!area.Contains(door.Tile) || content.IsExternalWall(existing)))
        {
            // Doors cannot be cut into walls that already stand outside the room, nor into the hospital's outer walls.
            return RoomPlanProblem.DoorInWall;
        }

        if (!map.Contains(outside.X, outside.Y) || !map[outside.X, outside.Y].Has(TileFlags.Passable | TileFlags.Hospital) || map[outside.X, outside.Y].Has(TileFlags.Room))
        {
            return RoomPlanProblem.DoorBlocked;
        }

        return swing ? CheckSwingDoor(map, content, area, door) : RoomPlanProblem.None;
    }

    /// <summary>
    /// The left swing door's edge beside the right one must pass the same checks, and it may not be the second tile from
    /// the top corner along a north or west wall (<c>UIEditRoom:screenToWall</c> skips it there).
    /// </summary>
    private static RoomPlanProblem CheckSwingDoor(LevelMap map, GameContent content, TileRect area, DoorPlacement door)
    {
        var slave = door.Edge == EdgeOrientation.North
            ? door with { Tile = door.Tile with { X = door.Tile.X - 1 } }
            : door with { Tile = door.Tile with { Y = door.Tile.Y - 1 } };
        var beyond = door.Edge == EdgeOrientation.North
            ? door with { Tile = door.Tile with { X = door.Tile.X + 1 } }
            : door with { Tile = door.Tile with { Y = door.Tile.Y + 1 } };
        if (OutsideTile(area, beyond) is null)
        {
            return RoomPlanProblem.DoorNotOnPerimeter;
        }

        var topWall = area.Contains(door.Tile);
        var start = door.Edge == EdgeOrientation.North ? area.X : area.Y;
        var index = door.Edge == EdgeOrientation.North ? slave.Tile.X : slave.Tile.Y;
        if (IsCorner(area, slave) || (topWall && index < start + 2))
        {
            return RoomPlanProblem.DoorInCorner;
        }

        return CheckDoor(map, content, area, slave);
    }

    /// <summary>
    /// Checks a window for an area and door that passed their checks. As in CorsixTH, windows go
    /// where no wall stands yet: not into the hospital's outer walls, nor into neighbouring rooms' walls.
    /// </summary>
    public static RoomPlanProblem CheckWindow(LevelMap map, RoomDefinition definition, TileRect area, DoorPlacement door, DoorPlacement window)
    {
        if (!definition.Walls.HasWindows)
        {
            return RoomPlanProblem.NoWindows;
        }

        if (OutsideTile(area, window) is null || !map.Contains(window.Tile.X, window.Tile.Y))
        {
            return RoomPlanProblem.WindowNotOnPerimeter;
        }

        if (IsCorner(area, window))
        {
            return RoomPlanProblem.WindowInCorner;
        }

        if (window == door)
        {
            return RoomPlanProblem.WindowOnDoor;
        }

        return WallAt(map, window) != 0 ? RoomPlanProblem.WindowInWall : RoomPlanProblem.None;
    }

    /// <summary>
    /// Whether an edge on the perimeter belongs to one of the room's corner tiles. Doors and windows
    /// never go there, as in the original game; CorsixTH notes that they would clash with wall shadows.
    /// </summary>
    public static bool IsCorner(TileRect area, DoorPlacement edge)
    {
        return edge.Edge == EdgeOrientation.North
            ? edge.Tile.X == area.X || edge.Tile.X == area.Right - 1
            : edge.Tile.Y == area.Y || edge.Tile.Y == area.Bottom - 1;
    }

    /// <summary>
    /// The wall edge the cursor picks for a door or window, following CorsixTH's <c>screenToWall</c>:
    /// the cursor may be on a perimeter tile or just outside it, and a corner tile gives the nearest
    /// edge that is not a corner, on whichever side of the tile the cursor is. Null away from the perimeter.
    /// </summary>
    /// <param name="area">The room's area, at least 3 by 3.</param>
    /// <param name="tile">The tile under the cursor.</param>
    /// <param name="fractionX">Where in the tile the cursor is along x, from 0 to 255.</param>
    /// <param name="fractionY">Where in the tile the cursor is along y, from 0 to 255.</param>
    public static DoorPlacement? WallNear(TileRect area, TilePosition tile, int fractionX = 128, int fractionY = 128)
    {
        if (area.Width < 3 || area.Height < 3)
        {
            return null;
        }

        var (x, y) = (tile.X, tile.Y);
        var (right, bottom) = (area.Right - 1, area.Bottom - 1);

        // On screen, the right half of a tile has x ahead of y, and its lower half has x + y past the middle.
        var screenRight = fractionX >= fractionY;
        var screenLower = fractionX + fractionY >= 256;
        if (x == area.X && y == area.Y)
        {
            return screenRight ? new DoorPlacement(new(x + 1, y), EdgeOrientation.North) : new DoorPlacement(new(x, y + 1), EdgeOrientation.West);
        }

        if (x == right && y == bottom)
        {
            return screenRight ? new DoorPlacement(new(area.Right, y - 1), EdgeOrientation.West) : new DoorPlacement(new(x - 1, area.Bottom), EdgeOrientation.North);
        }

        if (x == area.X && y == bottom)
        {
            return screenLower ? new DoorPlacement(new(x + 1, area.Bottom), EdgeOrientation.North) : new DoorPlacement(new(x, y - 1), EdgeOrientation.West);
        }

        if (x == right && y == area.Y)
        {
            return screenLower ? new DoorPlacement(new(area.Right, y + 1), EdgeOrientation.West) : new DoorPlacement(new(x - 1, y), EdgeOrientation.North);
        }

        var withinColumns = x >= area.X && x <= right;
        var withinRows = y >= area.Y && y <= bottom;
        var row = Math.Clamp(y, area.Y + 1, bottom - 1);
        var column = Math.Clamp(x, area.X + 1, right - 1);
        if (withinRows && (x == area.X - 1 || x == area.X))
        {
            return new DoorPlacement(new(area.X, row), EdgeOrientation.West);
        }

        if (withinColumns && (y == area.Y - 1 || y == area.Y))
        {
            return new DoorPlacement(new(column, area.Y), EdgeOrientation.North);
        }

        if (withinRows && (x == area.Right || x == right))
        {
            return new DoorPlacement(new(area.Right, row), EdgeOrientation.West);
        }

        if (withinColumns && (y == area.Bottom || y == bottom))
        {
            return new DoorPlacement(new(column, area.Bottom), EdgeOrientation.North);
        }

        return null;
    }

    /// <summary>Whether <paramref name="player"/> (0-based) owns the land the room stands on.</summary>
    public static bool IsOwnedBy(LevelMap map, Room room, int player) => map.GetParcelOwner(map[room.Area.X, room.Area.Y].Parcel) == player + 1;

    /// <summary>Needed objects that are not in the room yet, with how many of each are missing, ordered by object id.</summary>
    public static IReadOnlyList<(string ObjectType, int Count)> MissingObjects(Simulation simulation, Room room)
    {
        if (simulation.Content.FindRoom(room.RoomType)?.NeededObjects is not { } needed)
        {
            return [];
        }

        var placed = simulation.Entities.OfType<RoomObject>().Where(o => o.RoomId == room.Id).ToList();
        return needed
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (ObjectType: pair.Key, Count: pair.Value - placed.Count(o => o.ObjectType == pair.Key)))
            .Where(missing => missing.Count > 0)
            .ToList();
    }

    /// <summary>Whether <paramref name="player"/> may confirm the room: it is theirs, still being built and has everything it needs.</summary>
    public static bool CanConfirm(Simulation simulation, Room room, int player)
    {
        return !room.IsActive && !room.IsClosing && !room.IsCrashed && IsOwnedBy(simulation.Map, room, player) && MissingObjects(simulation, room).Count == 0;
    }

    /// <summary>
    /// Builds the room after a successful <see cref="Check"/>: walls around the area (keeping
    /// outer walls and walls of neighbouring rooms), windows, the room's floor, the door, and updated
    /// travel flags and shadows. Follows CorsixTH's <c>UIEditRoom:_finishRoomFoundation</c>. The new
    /// room is inactive until its owner confirms it.
    /// </summary>
    internal static Room Build(Simulation simulation, RoomPlan plan, RoomDefinition definition)
    {
        var map = simulation.Map;
        var area = plan.Area;
        var walls = definition.Walls;

        var original = new OriginalTile[area.Width * area.Height];
        for (var y = area.Y; y < area.Bottom; y++)
        {
            for (var x = area.X; x < area.Right; x++)
            {
                var tile = map[x, y];
                original[((y - area.Y) * area.Width) + (x - area.X)] = new OriginalTile(tile.Floor, tile.Flags & Room.RestorableFlags);
            }
        }

        var outside = OutsideTile(area, plan.Door)!.Value;
        var room = simulation.AddEntity(id => new Room(id, definition.Id, area, original)
        {
            DoorOutsideWasBuildable = map[outside.X, outside.Y].Has(TileFlags.Buildable),
        });

        // Room:Room's World:prepareRectangleTilesForBuild: litter goes from the room's area, rat holes from its edge.
        for (var x = area.X; x < area.Right; x++)
        {
            for (var y = area.Y; y < area.Bottom; y++)
            {
                Litter.RemoveAllAt(simulation, new TilePosition(x, y));
                if (x == area.X || x == area.Right - 1 || y == area.Y || y == area.Bottom - 1)
                {
                    Disasters.Rats.RemoveRatholesAt(simulation, new TilePosition(x, y));
                }
            }
        }

        for (var x = area.X; x < area.Right; x++)
        {
            SetInsideWall(simulation, walls, ref map[x, area.Y], north: true);
            if (area.Bottom < map.Height)
            {
                SetOutsideWall(ref map[x, area.Bottom], north: true, walls.OutsideNorth);
            }
        }

        for (var y = area.Y; y < area.Bottom; y++)
        {
            SetInsideWall(simulation, walls, ref map[area.X, y], north: false);
            if (area.Right < map.Width)
            {
                SetOutsideWall(ref map[area.Right, y], north: false, walls.OutsideWest);
            }
        }

        foreach (var window in plan.WindowList)
        {
            ref var tile = ref map[window.Tile.X, window.Tile.Y];
            if (window.Edge == EdgeOrientation.North)
            {
                tile.NorthWall = walls.WindowNorth;
            }
            else
            {
                tile.WestWall = walls.WindowWest;
            }
        }

        for (var y = area.Y; y < area.Bottom; y++)
        {
            for (var x = area.X; x < area.Right; x++)
            {
                ref var tile = ref map[x, y];
                tile.Floor = definition.FloorSprite;
                tile.RoomId = room.Id;
                tile.Set(TileFlags.Room, true);
            }
        }

        var door = simulation.AddEntity(id => new RoomDoor(id, plan.Door.Tile, plan.Door.Edge, room.Id, definition.SwingDoors));
        door.Place(map);
        if (door.IsSwing)
        {
            SetSwingWalls(map, walls, area, door);
        }
        room.DoorId = door.Id;

        map.UpdatePathfinding();
        map.UpdateShadows();
        return room;
    }

    /// <summary>
    /// Removes a room again: its objects, its door, its walls (keeping outer walls and giving walls
    /// shared with a neighbouring room back to that room) and its floor, as CorsixTH's
    /// <c>UIEditRoom:removeRoom</c> does when a room under construction is cancelled.
    /// </summary>
    internal static void Remove(Simulation simulation, Room room)
    {
        var map = simulation.Map;
        var area = room.Area;
        foreach (var id in simulation.Entities.OfType<RoomObject>().Where(o => o.RoomId == room.Id).Select(o => o.Id).ToList())
        {
            simulation.RemoveEntity(id);
        }

        if (simulation.FindEntity(room.DoorId) is RoomDoor door)
        {
            door.Unplace(map, room.DoorOutsideWasBuildable, area);
            simulation.RemoveEntity(door.Id);
        }

        for (var x = area.X; x < area.Right; x++)
        {
            ClearWall(simulation, room, new(x, area.Y), north: true, new(x, area.Y - 1), shared: true);
            ClearWall(simulation, room, new(x, area.Bottom), north: true, new(x, area.Bottom), shared: false);
        }

        for (var y = area.Y; y < area.Bottom; y++)
        {
            ClearWall(simulation, room, new(area.X, y), north: false, new(area.X - 1, y), shared: true);
            ClearWall(simulation, room, new(area.Right, y), north: false, new(area.Right, y), shared: false);
        }

        for (var y = area.Y; y < area.Bottom; y++)
        {
            for (var x = area.X; x < area.Right; x++)
            {
                ref var tile = ref map[x, y];
                var before = room.OriginalTileAt(new(x, y));
                tile.Floor = before.Floor;
                tile.Flags = (tile.Flags & ~Room.RestorableFlags) | before.Flags;
                tile.ObjectId = 0;
                tile.RoomId = 0;
                tile.Set(TileFlags.Room, false);
            }
        }

        simulation.RemoveEntity(room.Id);
        map.UpdatePathfinding();
        map.UpdateShadows();
    }

    /// <summary>The tile on the far side of the door from the room, or null if the door is not on the perimeter.</summary>
    internal static TilePosition? OutsideTile(TileRect area, DoorPlacement door)
    {
        var tile = door.Tile;
        if (door.Edge == EdgeOrientation.North && tile.X >= area.X && tile.X < area.Right)
        {
            if (tile.Y == area.Y)
            {
                return tile with { Y = tile.Y - 1 };
            }

            if (tile.Y == area.Bottom)
            {
                return tile;
            }
        }

        if (door.Edge == EdgeOrientation.West && tile.Y >= area.Y && tile.Y < area.Bottom)
        {
            if (tile.X == area.X)
            {
                return tile with { X = tile.X - 1 };
            }

            if (tile.X == area.Right)
            {
                return tile;
            }
        }

        return null;
    }

    private static ushort WallAt(LevelMap map, DoorPlacement edge)
    {
        if (!map.Contains(edge.Tile.X, edge.Tile.Y))
        {
            return 0;
        }

        var tile = map[edge.Tile.X, edge.Tile.Y];
        return edge.Edge == EdgeOrientation.North ? tile.NorthWall : tile.WestWall;
    }

    /// <summary>
    /// <c>UIEditRoom</c>'s swing door pieces: the wall behind the left door becomes the left swing piece and the wall
    /// beyond the right door the right one, inside pieces in the room's own walls and outside ones in its south and east.
    /// </summary>
    private static void SetSwingWalls(LevelMap map, WallSet walls, TileRect area, RoomDoor door)
    {
        if (walls.Swing is not { } swing)
        {
            return;
        }

        var north = door.Edge == EdgeOrientation.North;
        var inside = area.Contains(door.Tile);
        foreach (var (tile, left) in new[] { (door.SlaveTile, true), (door.BeyondTile, false) })
        {
            if (!map.Contains(tile.X, tile.Y))
            {
                continue;
            }

            var sprite = (inside, north, left) switch
            {
                (true, true, true) => swing.InsideNorthLeft,
                (true, true, false) => swing.InsideNorthRight,
                (true, false, true) => swing.InsideWestLeft,
                (true, false, false) => swing.InsideWestRight,
                (false, true, true) => swing.OutsideNorthLeft,
                (false, true, false) => swing.OutsideNorthRight,
                (false, false, true) => swing.OutsideWestLeft,
                _ => swing.OutsideWestRight,
            };
            if (north)
            {
                map[tile.X, tile.Y].NorthWall = sprite;
            }
            else
            {
                map[tile.X, tile.Y].WestWall = sprite;
            }
        }
    }

    /// <summary>
    /// Inside walls replace whatever stands there, except the hospital's outer walls. A neighbouring
    /// room's window stays a window, as in CorsixTH.
    /// </summary>
    private static void SetInsideWall(Simulation simulation, WallSet walls, ref MapTile tile, bool north)
    {
        var existing = north ? tile.NorthWall : tile.WestWall;
        if (existing != 0 && simulation.Content.IsExternalWall(existing))
        {
            return;
        }

        var window = walls.HasWindows && simulation.Content.IsWindowWall(existing);
        if (north)
        {
            tile.NorthWall = window ? walls.WindowNorth : walls.InsideNorth;
        }
        else
        {
            tile.WestWall = window ? walls.WindowWest : walls.InsideWest;
        }
    }

    /// <summary>Outside walls are only added where no wall stands yet (for example next to another room).</summary>
    private static void SetOutsideWall(ref MapTile tile, bool north, ushort sprite)
    {
        if (north && tile.NorthWall == 0)
        {
            tile.NorthWall = sprite;
        }
        else if (!north && tile.WestWall == 0)
        {
            tile.WestWall = sprite;
        }
    }

    /// <summary>
    /// Takes down one wall of a removed room, as CorsixTH's <c>_remove_wall_line</c>: outer walls stay;
    /// a wall next to another room stays, and where this room had replaced it (<paramref name="shared"/>:
    /// its north and west sides) it becomes that room's outside wall again.
    /// </summary>
    private static void ClearWall(Simulation simulation, Room room, TilePosition at, bool north, TilePosition beyond, bool shared)
    {
        var map = simulation.Map;
        if (!map.Contains(at.X, at.Y))
        {
            return;
        }

        ref var tile = ref map[at.X, at.Y];
        var existing = north ? tile.NorthWall : tile.WestWall;
        if (existing != 0 && simulation.Content.IsExternalWall(existing))
        {
            return;
        }

        var neighbourId = map.Contains(beyond.X, beyond.Y) ? map[beyond.X, beyond.Y].RoomId : 0;
        ushort replacement = 0;
        if (neighbourId != 0 && neighbourId != room.Id
            && simulation.FindEntity(neighbourId) is Room neighbour
            && simulation.Content.FindRoom(neighbour.RoomType) is { } definition)
        {
            if (!shared)
            {
                return;
            }

            var window = definition.Walls.HasWindows && simulation.Content.IsWindowWall(existing);
            replacement = north
                ? (window ? definition.Walls.WindowNorth : definition.Walls.OutsideNorth)
                : (window ? definition.Walls.WindowWest : definition.Walls.OutsideWest);
        }

        if (north)
        {
            tile.NorthWall = replacement;
        }
        else
        {
            tile.WestWall = replacement;
        }
    }
}

/// <summary>Reads and writes room plans, in commands and saved games.</summary>
internal static class RoomPlanSerializer
{
    public static RoomPlan Read(SimReader reader)
    {
        var roomType = reader.ReadString();
        var area = new TileRect(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        var door = ReadEdge(reader);
        var windows = new DoorPlacement[reader.ReadCount(RoomPlan.MaxWindows)];
        for (var i = 0; i < windows.Length; i++)
        {
            windows[i] = ReadEdge(reader);
        }

        return new RoomPlan(roomType, area, door, windows);
    }

    public static void Write(SimWriter writer, RoomPlan plan)
    {
        writer.WriteString(plan.RoomType);
        writer.WriteInt32(plan.Area.X);
        writer.WriteInt32(plan.Area.Y);
        writer.WriteInt32(plan.Area.Width);
        writer.WriteInt32(plan.Area.Height);
        WriteEdge(writer, plan.Door);
        writer.WriteInt32(plan.WindowList.Count);
        foreach (var window in plan.WindowList)
        {
            WriteEdge(writer, window);
        }
    }

    private static DoorPlacement ReadEdge(SimReader reader) => new(reader.ReadTile(), (EdgeOrientation)reader.ReadByte());

    private static void WriteEdge(SimWriter writer, DoorPlacement edge)
    {
        writer.WriteTile(edge.Tile);
        writer.WriteByte((byte)edge.Edge);
    }
}
