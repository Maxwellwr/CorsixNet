using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;

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
/// Where a room's door goes: a tile edge on the room's perimeter. A door on the north or west
/// side of the room lies on an edge of a tile inside the room; a door on the south or east side
/// lies on the north or west edge of the tile just outside.
/// </summary>
public readonly record struct DoorPlacement(TilePosition Tile, EdgeOrientation Edge);

/// <summary>A room the player wants to build.</summary>
public sealed record RoomPlan(string RoomType, TileRect Area, DoorPlacement Door);

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
    DoorInWall,
    DoorBlocked,
}

/// <summary>
/// Checks room plans and builds rooms. The client uses the same checks for its preview, so
/// a plan that looks valid is accepted when the command executes on every machine.
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
            }
        }

        return RoomPlanProblem.None;
    }

    /// <summary>Checks a complete plan for <paramref name="player"/> (0-based).</summary>
    public static RoomPlanProblem Check(LevelMap map, GameContent content, RoomPlan plan, int player)
    {
        var area = plan.Area;
        var problem = CheckArea(map, content.FindRoom(plan.RoomType), area, player);
        if (problem != RoomPlanProblem.None)
        {
            return problem;
        }

        if (OutsideTile(area, plan.Door) is not { } outside)
        {
            return RoomPlanProblem.DoorNotOnPerimeter;
        }

        var door = plan.Door.Tile;
        var existing = plan.Door.Edge == EdgeOrientation.North ? map[door.X, door.Y].NorthWall : map[door.X, door.Y].WestWall;
        if (existing != 0 && !IsInsideEdge(area, plan.Door))
        {
            // Doors cannot be cut into walls that already stand outside the room.
            return RoomPlanProblem.DoorInWall;
        }

        if (existing != 0 && content.IsExternalWall(existing))
        {
            return RoomPlanProblem.DoorInWall;
        }

        if (!map.Contains(outside.X, outside.Y) || !map[outside.X, outside.Y].Has(TileFlags.Passable | TileFlags.Hospital) || map[outside.X, outside.Y].Has(TileFlags.Room))
        {
            return RoomPlanProblem.DoorBlocked;
        }

        return RoomPlanProblem.None;
    }

    /// <summary>
    /// The door placement on the room's perimeter nearest to <paramref name="tile"/>, or null if
    /// the tile is not on or just outside the perimeter. Used by the client to follow the mouse.
    /// </summary>
    public static DoorPlacement? DoorNear(TileRect area, TilePosition tile)
    {
        var withinColumns = tile.X >= area.X && tile.X < area.Right;
        var withinRows = tile.Y >= area.Y && tile.Y < area.Bottom;
        if (withinColumns && (tile.Y == area.Y || tile.Y == area.Y - 1))
        {
            return new DoorPlacement(tile with { Y = area.Y }, EdgeOrientation.North);
        }

        if (withinColumns && (tile.Y == area.Bottom || tile.Y == area.Bottom - 1))
        {
            return new DoorPlacement(tile with { Y = area.Bottom }, EdgeOrientation.North);
        }

        if (withinRows && (tile.X == area.X || tile.X == area.X - 1))
        {
            return new DoorPlacement(tile with { X = area.X }, EdgeOrientation.West);
        }

        if (withinRows && (tile.X == area.Right || tile.X == area.Right - 1))
        {
            return new DoorPlacement(tile with { X = area.Right }, EdgeOrientation.West);
        }

        return null;
    }

    /// <summary>
    /// Builds the room after a successful <see cref="Check"/>: walls around the area (keeping
    /// outer walls and walls of neighbouring rooms), the room's floor, the door, and updated
    /// travel flags and shadows. Follows CorsixTH's <c>UIEditRoom:_finishRoomFoundation</c>.
    /// </summary>
    internal static Room Build(Simulation simulation, RoomPlan plan, RoomDefinition definition)
    {
        var map = simulation.Map;
        var area = plan.Area;
        var walls = definition.Walls;
        var room = simulation.AddEntity(id => new Room(id, definition.Id, area));

        for (var x = area.X; x < area.Right; x++)
        {
            SetInsideWall(simulation, ref map[x, area.Y], north: true, walls.InsideNorth);
            if (area.Bottom < map.Height)
            {
                SetOutsideWall(ref map[x, area.Bottom], north: true, walls.OutsideNorth);
            }
        }

        for (var y = area.Y; y < area.Bottom; y++)
        {
            SetInsideWall(simulation, ref map[area.X, y], north: false, walls.InsideWest);
            if (area.Right < map.Width)
            {
                SetOutsideWall(ref map[area.Right, y], north: false, walls.OutsideWest);
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

        var door = simulation.AddEntity(id => new RoomDoor(id, plan.Door.Tile, plan.Door.Edge, room.Id));
        door.Place(map);
        room.DoorId = door.Id;

        map.UpdatePathfinding();
        map.UpdateShadows();
        return room;
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

    private static bool IsInsideEdge(TileRect area, DoorPlacement door) => area.Contains(door.Tile);

    /// <summary>Inside walls replace whatever stands there, except the hospital's outer walls.</summary>
    private static void SetInsideWall(Simulation simulation, ref MapTile tile, bool north, ushort sprite)
    {
        var existing = north ? tile.NorthWall : tile.WestWall;
        if (existing != 0 && simulation.Content.IsExternalWall(existing))
        {
            return;
        }

        if (north)
        {
            tile.NorthWall = sprite;
        }
        else
        {
            tile.WestWall = sprite;
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
}
