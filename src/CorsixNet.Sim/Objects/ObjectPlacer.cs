// Placement rules ported from CorsixTH CorsixTH/Lua/dialogs/place_objects.lua, CorsixTH/Lua/world.lua
// (isFootprintTileBuildableOrPassable) and CorsixTH/Lua/entities/object.lua (occupyTilesByObjectFootprintAt).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;

namespace CorsixNet.Sim.Objects;

/// <summary>Why an object cannot be placed.</summary>
public enum ObjectPlacementProblem
{
    None,
    UnknownObject,
    NoSuchOrientation,
    OutsideMap,
    NotAllowedHere,
    NotOwned,
    Blocked,
    WouldBlockAccess,
}

/// <summary>
/// Checks and performs object placement. The client uses <see cref="Check"/> for its preview,
/// so a placement shown as valid is accepted when the command executes.
/// </summary>
public static class ObjectPlacer
{
    private static readonly (Direction Side, int Dx, int Dy, TileFlags Buildable, FootprintFlags Needed)[] Sides =
    [
        (Direction.North, 0, -1, TileFlags.BuildableNorth, FootprintFlags.NeedNorthSide),
        (Direction.East, 1, 0, TileFlags.BuildableEast, FootprintFlags.NeedEastSide),
        (Direction.South, 0, 1, TileFlags.BuildableSouth, FootprintFlags.NeedSouthSide),
        (Direction.West, -1, 0, TileFlags.BuildableWest, FootprintFlags.NeedWestSide),
    ];

    /// <summary>Checks placing <paramref name="objectId"/> at <paramref name="tile"/> facing <paramref name="orientation"/> for <paramref name="player"/> (0-based).</summary>
    public static ObjectPlacementProblem Check(Simulation simulation, string objectId, TilePosition tile, Direction orientation, int player)
    {
        var map = simulation.Map;
        if (simulation.Content.FindObject(objectId) is not { } definition)
        {
            return ObjectPlacementProblem.UnknownObject;
        }

        if (!definition.Orientations.TryGetValue(orientation, out var layout))
        {
            return ObjectPlacementProblem.NoSuchOrientation;
        }

        if (!map.Contains(tile.X, tile.Y))
        {
            return ObjectPlacementProblem.OutsideMap;
        }

        var roomId = map[tile.X, tile.Y].RoomId;
        if (!IsAllowedIn(simulation, definition, roomId))
        {
            return ObjectPlacementProblem.NotAllowedHere;
        }

        foreach (var part in layout.Footprint)
        {
            var x = tile.X + part.X;
            var y = tile.Y + part.Y;
            if (!map.Contains(x, y))
            {
                return ObjectPlacementProblem.OutsideMap;
            }

            var cell = map[x, y];
            if (cell.RoomId != roomId)
            {
                return ObjectPlacementProblem.NotAllowedHere;
            }

            if (map.GetParcelOwner(cell.Parcel) != player + 1)
            {
                return ObjectPlacementProblem.NotOwned;
            }

            if (!IsTileFree(cell, part, layout.Footprint))
            {
                return ObjectPlacementProblem.Blocked;
            }
        }

        if (roomId != 0 && !KeepsRoomAccessible(simulation, roomId, tile, layout))
        {
            return ObjectPlacementProblem.WouldBlockAccess;
        }

        return ObjectPlacementProblem.None;
    }

    /// <summary>Places the object after a successful <see cref="Check"/>.</summary>
    internal static RoomObject Place(Simulation simulation, ObjectDefinition definition, TilePosition tile, Direction orientation)
    {
        var map = simulation.Map;
        var roomId = map[tile.X, tile.Y].RoomId;
        var placed = simulation.AddEntity(id => new RoomObject(id, definition.Id, tile, orientation, roomId));
        Occupy(map, tile, definition.Orientations[orientation], placed.Id);
        return placed;
    }

    /// <summary>
    /// The footprint tile must be passable (for use positions) or buildable (otherwise), and every side
    /// the object covers must still be buildable: sides towards other footprint tiles, all sides of a
    /// complete cell, and sides the object explicitly needs.
    /// </summary>
    private static bool IsTileFree(MapTile cell, FootprintTile part, IReadOnlyList<FootprintTile> footprint)
    {
        var required = part.IsUsePosition ? TileFlags.Passable : TileFlags.Buildable;
        if (!cell.Has(required))
        {
            return false;
        }

        // A use position may not overlap another object's use position or a door's walkway,
        // which are no longer buildable, unless it is shareable.
        if (part.IsUsePosition && (part.Flags & FootprintFlags.Shareable) == 0 && !cell.Has(TileFlags.Buildable))
        {
            return false;
        }

        foreach (var side in Sides)
        {
            if (CoversSide(part, footprint, side.Dx, side.Dy, side.Needed) && !cell.Has(side.Buildable))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CoversSide(FootprintTile part, IReadOnlyList<FootprintTile> footprint, int dx, int dy, FootprintFlags needed)
    {
        return (part.Flags & (FootprintFlags.CompleteCell | needed)) != 0
            || footprint.Any(other => other.X == part.X + dx && other.Y == part.Y + dy);
    }

    /// <summary>Marks the footprint on the map, as CorsixTH's <c>occupyTilesByObjectFootprintAt</c> does.</summary>
    private static void Occupy(LevelMap map, TilePosition tile, ObjectOrientation layout, int objectId)
    {
        foreach (var part in layout.Footprint)
        {
            ref var cell = ref map[tile.X + part.X, tile.Y + part.Y];
            cell.Set(TileFlags.Buildable, false);
            cell.Set(TileFlags.Passable, part.IsUsePosition);
            if (!part.IsUsePosition)
            {
                cell.ObjectId = objectId;
            }

            foreach (var side in Sides)
            {
                if (CoversSide(part, layout.Footprint, side.Dx, side.Dy, side.Needed))
                {
                    cell.Set(side.Buildable, false);
                }
            }
        }
    }

    private static bool IsAllowedIn(Simulation simulation, ObjectDefinition definition, int roomId)
    {
        if (roomId == 0)
        {
            return definition.CorridorObject;
        }

        return simulation.FindEntity(roomId) is Room room
            && simulation.Content.FindRoom(room.RoomType) is { } roomDefinition
            && roomDefinition.Allows(definition.Id);
    }

    /// <summary>
    /// Whether, with the object placed, every use position in the room (including the new object's)
    /// can still be reached from the tile inside the room's door.
    /// </summary>
    private static bool KeepsRoomAccessible(Simulation simulation, int roomId, TilePosition tile, ObjectOrientation layout)
    {
        var map = simulation.Map;
        if (simulation.FindEntity(roomId) is not Room room || simulation.FindEntity(room.DoorId) is not RoomDoor door)
        {
            return false;
        }

        // Try the placement on copies of the affected tiles, then put them back.
        var saved = layout.Footprint.Select(part => (part, cell: map[tile.X + part.X, tile.Y + part.Y])).ToList();
        Occupy(map, tile, layout, objectId: -1);
        try
        {
            var start = door.InsideTile(room.Area);
            var reachable = ReachableTiles(map, start, room.Area);
            var targets = simulation.Entities.OfType<RoomObject>()
                .Where(o => o.RoomId == roomId)
                .SelectMany(o => UsePositions(simulation, o))
                .Concat(layout.Footprint.Where(p => p.IsUsePosition).Select(p => new TilePosition(tile.X + p.X, tile.Y + p.Y)));
            return targets.All(target => room.Area.Contains(target) && reachable[((target.Y - room.Area.Y) * room.Area.Width) + (target.X - room.Area.X)]);
        }
        finally
        {
            foreach (var (part, cell) in saved)
            {
                map[tile.X + part.X, tile.Y + part.Y] = cell;
            }
        }
    }

    /// <summary>Use positions of a placed object, in map coordinates.</summary>
    public static IEnumerable<TilePosition> UsePositions(Simulation simulation, RoomObject placed)
    {
        if (simulation.Content.FindObject(placed.ObjectType) is not { } definition
            || !definition.Orientations.TryGetValue(placed.Orientation, out var layout))
        {
            yield break;
        }

        foreach (var part in layout.Footprint)
        {
            if (part.IsUsePosition)
            {
                yield return new TilePosition(placed.Tile.X + part.X, placed.Tile.Y + part.Y);
            }
        }
    }

    /// <summary>Flood fill over passable tiles of the room's area, following travel flags.</summary>
    private static bool[] ReachableTiles(LevelMap map, TilePosition start, TileRect area)
    {
        var reachable = new bool[area.Width * area.Height];
        if (!area.Contains(start) || !map[start.X, start.Y].Has(TileFlags.Passable))
        {
            return reachable;
        }

        var queue = new Queue<TilePosition>();
        queue.Enqueue(start);
        reachable[((start.Y - area.Y) * area.Width) + (start.X - area.X)] = true;
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var cell = map[current.X, current.Y];
            foreach (var side in Sides)
            {
                var travel = side.Side switch
                {
                    Direction.North => TileFlags.CanTravelNorth,
                    Direction.East => TileFlags.CanTravelEast,
                    Direction.South => TileFlags.CanTravelSouth,
                    _ => TileFlags.CanTravelWest,
                };
                var next = new TilePosition(current.X + side.Dx, current.Y + side.Dy);
                if (!cell.Has(travel) || !area.Contains(next))
                {
                    continue;
                }

                var index = ((next.Y - area.Y) * area.Width) + (next.X - area.X);
                if (!reachable[index] && map[next.X, next.Y].Has(TileFlags.Passable))
                {
                    reachable[index] = true;
                    queue.Enqueue(next);
                }
            }
        }

        return reachable;
    }
}
