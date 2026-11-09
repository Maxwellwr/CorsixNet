// Placement rules ported from CorsixTH CorsixTH/Lua/dialogs/place_objects.lua
// (_placementAvailableHereAndOtherObjectsDoNotInterfere, _isSideObjectPlacementValid), CorsixTH/Lua/world.lua
// (isFootprintTileBuildableOrPassable, isTileExclusivelyPassable, wouldNonSideObjectBreakPathfindingIfSpawnedAt)
// and CorsixTH/Lua/entities/object.lua (occupyTilesByObjectFootprintAt, adjacent_to_solid_footprint).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;
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
    /// <summary>The plant's object id: placed plants get a <see cref="PlantState"/>.</summary>
    private const string PlantType = "plant";

    private static readonly (Direction Side, int Dx, int Dy, TileFlags Buildable, FootprintFlags Needed)[] Sides =
    [
        (Direction.North, 0, -1, TileFlags.BuildableNorth, FootprintFlags.NeedNorthSide),
        (Direction.East, 1, 0, TileFlags.BuildableEast, FootprintFlags.NeedEastSide),
        (Direction.South, 0, 1, TileFlags.BuildableSouth, FootprintFlags.NeedSouthSide),
        (Direction.West, -1, 0, TileFlags.BuildableWest, FootprintFlags.NeedWestSide),
    ];

    /// <summary>The order CorsixTH looks at the tiles around a solid footprint tile (<c>adjacent_to_solid_footprint</c>).</summary>
    private static readonly (int Dx, int Dy)[] AdjacentDeltas = [(-1, 0), (0, -1), (0, 1), (1, 0)];

    /// <summary>The bin's object type ("thob"): facing east, it stands against the west side (CorsixTH's special case).</summary>
    private const byte BinObjectType = 50;

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

            var position = new TilePosition(x, y);
            if (part.IsSide)
            {
                if (!cell.Has(SideOf(definition, orientation).Buildable))
                {
                    return ObjectPlacementProblem.Blocked;
                }
            }
            else if (!IsTileFree(cell, part, layout.Footprint))
            {
                return ObjectPlacementProblem.Blocked;
            }

            // Only one object per tile, and not on a tile someone has to stand on (CorsixTH's check 4).
            if (!part.IsShareable && (cell.ObjectId != 0 || !IsTileExclusivelyPassable(simulation, position)))
            {
                return ObjectPlacementProblem.Blocked;
            }
        }

        var side = layout.Footprint.Any(part => part.IsSide);
        if (roomId != 0)
        {
            if (!side && !KeepsRoomAccessible(simulation, roomId, tile, layout))
            {
                return ObjectPlacementProblem.WouldBlockAccess;
            }
        }
        else if (side ? !SideObjectKeepsPaths(simulation, tile, SideOf(definition, orientation)) : WouldBreakPathfinding(simulation, tile, layout))
        {
            return ObjectPlacementProblem.WouldBlockAccess;
        }

        return ObjectPlacementProblem.None;
    }

    /// <summary>
    /// The side of the tile a side object stands against: the side it faces, except that the bin facing east
    /// stands against the west side, as CorsixTH has it.
    /// </summary>
    private static (Direction Side, int Dx, int Dy, TileFlags Buildable, FootprintFlags Needed) SideOf(ObjectDefinition definition, Direction orientation)
    {
        if (definition.ObjectType == BinObjectType && orientation == Direction.East)
        {
            orientation = Direction.West;
        }

        return Sides[(int)orientation];
    }

    /// <summary>
    /// Whether nobody has to stand on the tile: it is no use position of an object (unless shareable) and no
    /// door's walkway, as CorsixTH's <c>World:isTileExclusivelyPassable</c>.
    /// </summary>
    private static bool IsTileExclusivelyPassable(Simulation simulation, TilePosition tile)
    {
        foreach (var entity in simulation.Entities)
        {
            switch (entity)
            {
                case RoomObject placed
                    when simulation.Content.FindObject(placed.ObjectType) is { } definition
                    && definition.Orientations.TryGetValue(placed.Orientation, out var layout):
                    foreach (var part in layout.Footprint)
                    {
                        if (part.IsUsePosition && !part.IsShareable && placed.Tile.X + part.X == tile.X && placed.Tile.Y + part.Y == tile.Y)
                        {
                            return false;
                        }
                    }

                    break;
                case RoomDoor door when door.Tile == tile || door.OtherSide == tile:
                    return false;
                case EntranceDoor entrance when entrance.Tile == tile || OtherSide(entrance.Tile, entrance.Orientation) == tile:
                    return false;
            }
        }

        return true;
    }

    private static TilePosition OtherSide(TilePosition tile, EdgeOrientation edge) =>
        edge == EdgeOrientation.North ? tile with { Y = tile.Y - 1 } : tile with { X = tile.X - 1 };

    /// <summary>
    /// Whether a side object against an open edge (one without a wall) still lets people get from one side of the
    /// edge to the other some other way, as CorsixTH's <c>_isSideObjectPlacementValid</c> outside rooms. Against a
    /// wall it always does.
    /// </summary>
    private static bool SideObjectKeepsPaths(Simulation simulation, TilePosition tile, (Direction Side, int Dx, int Dy, TileFlags Buildable, FootprintFlags Needed) side)
    {
        var map = simulation.Map;
        var travel = TravelFlag(side.Side);
        if (!map[tile.X, tile.Y].Has(travel))
        {
            return true;
        }

        var beyond = new TilePosition(tile.X + side.Dx, tile.Y + side.Dy);
        var back = TravelFlag((Direction)(((int)side.Side + 2) % 4));
        map[tile.X, tile.Y].Set(travel, false);
        map[beyond.X, beyond.Y].Set(back, false);
        try
        {
            // As CorsixTH, no path only counts when the tile beyond is passable at all.
            return simulation.Pathfinder.TryFindPath(map, tile, beyond, []) || !map[beyond.X, beyond.Y].Has(TileFlags.Passable);
        }
        finally
        {
            map[tile.X, tile.Y].Set(travel, true);
            map[beyond.X, beyond.Y].Set(back, true);
        }
    }

    /// <summary>
    /// Whether an object outside rooms would cut the tiles around its solid part off from each other, as CorsixTH's
    /// <c>World:wouldNonSideObjectBreakPathfindingIfSpawnedAt</c>: with the solid tiles made impassable, each passable
    /// corridor tile next to them must still reach the previous one, unless one of the two is cut off from the
    /// hospital anyway.
    /// </summary>
    /// <remarks>
    /// CorsixTH's <c>isIsolated</c> makes the solid tiles passable again when it is done, so the tiles looked at after
    /// its first use are checked as if the object were not there. That looks like a slip; here the solid tiles stay
    /// impassable for the whole check.
    /// </remarks>
    private static bool WouldBreakPathfinding(Simulation simulation, TilePosition tile, ObjectOrientation layout)
    {
        var map = simulation.Map;
        var solid = layout.Footprint.Where(part => !part.IsUsePosition && !part.IsSide).Select(part => (part.X, part.Y)).ToList();
        var adjacent = new List<(int X, int Y)>();
        foreach (var (sx, sy) in solid)
        {
            foreach (var (dx, dy) in AdjacentDeltas)
            {
                var point = (sx + dx, sy + dy);
                if (!solid.Contains(point) && !adjacent.Contains(point))
                {
                    adjacent.Add(point);
                }
            }
        }

        var saved = solid.Select(point => map[tile.X + point.X, tile.Y + point.Y].Has(TileFlags.Passable)).ToList();
        foreach (var (sx, sy) in solid)
        {
            map[tile.X + sx, tile.Y + sy].Set(TileFlags.Passable, false);
        }

        try
        {
            TilePosition? previous = null;
            foreach (var (ax, ay) in adjacent)
            {
                var position = new TilePosition(tile.X + ax, tile.Y + ay);
                if (!map.Contains(position.X, position.Y) || map[position.X, position.Y].RoomId != 0 || !map[position.X, position.Y].Has(TileFlags.Passable))
                {
                    continue;
                }

                if (previous is { } last && !simulation.Pathfinder.TryFindPath(map, position, last, []))
                {
                    // No route between the two: unless either was cut off from the hospital before, the object breaks one.
                    if (IsReachableFromHospital(map, position))
                    {
                        if (IsReachableFromHospital(map, last))
                        {
                            return true;
                        }
                    }
                    else
                    {
                        position = last;
                    }
                }

                previous = position;
            }

            return false;
        }
        finally
        {
            for (var i = 0; i < solid.Count; i++)
            {
                map[tile.X + solid[i].X, tile.Y + solid[i].Y].Set(TileFlags.Passable, saved[i]);
            }
        }
    }

    /// <summary>Whether a hospital tile can be walked to from <paramref name="start"/> (CorsixTH's <c>isReachableFromHospital</c>).</summary>
    private static bool IsReachableFromHospital(LevelMap map, TilePosition start)
    {
        if (!map[start.X, start.Y].Has(TileFlags.Passable))
        {
            return false;
        }

        var seen = new HashSet<TilePosition> { start };
        var queue = new Queue<TilePosition>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var cell = map[current.X, current.Y];
            if (cell.Has(TileFlags.Hospital))
            {
                return true;
            }

            foreach (var side in Sides)
            {
                var next = new TilePosition(current.X + side.Dx, current.Y + side.Dy);
                if (cell.Has(TravelFlag(side.Side)) && map[next.X, next.Y].Has(TileFlags.Passable) && seen.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return false;
    }

    private static TileFlags TravelFlag(Direction direction) => direction switch
    {
        Direction.North => TileFlags.CanTravelNorth,
        Direction.East => TileFlags.CanTravelEast,
        Direction.South => TileFlags.CanTravelSouth,
        _ => TileFlags.CanTravelWest,
    };

    /// <summary>Places the object after a successful <see cref="Check"/>.</summary>
    internal static RoomObject Place(Simulation simulation, ObjectDefinition definition, TilePosition tile, Direction orientation)
    {
        var map = simulation.Map;
        var roomId = map[tile.X, tile.Y].RoomId;

        // World:prepareFootprintTilesForBuild: litter and rat holes go from the tiles the object takes up.
        foreach (var part in definition.Orientations[orientation].Footprint)
        {
            if ((part.Flags & FootprintFlags.CompleteCell) != 0 || !part.IsUsePosition)
            {
                Litter.RemoveAllAt(simulation, new TilePosition(tile.X + part.X, tile.Y + part.Y));
                Disasters.Rats.RemoveRatholesAt(simulation, new TilePosition(tile.X + part.X, tile.Y + part.Y));
            }
        }

        var placed = simulation.AddEntity(id => new RoomObject(id, definition.Id, tile, orientation, roomId));
        if (definition.Id == PlantType)
        {
            placed.Plant = new PlantState();
        }

        if (definition.Orientations[orientation].Footprint.Any(part => part.IsSide))
        {
            OccupySide(map, tile, SideOf(definition, orientation), placed.Id);
        }
        else
        {
            Occupy(map, tile, definition.Orientations[orientation], placed.Id);
        }

        ReceptionDesks.OnPlaced(simulation, placed);
        return placed;
    }

    /// <summary>Whether <paramref name="player"/> may pick up the object: it stands in a room of theirs that is still being built.</summary>
    public static bool CanPickUp(Simulation simulation, int objectId, int player)
    {
        return simulation.FindEntity(objectId) is RoomObject placed
            && simulation.FindEntity(placed.RoomId) is Room { IsActive: false } room
            && RoomBuilder.IsOwnedBy(simulation.Map, room, player);
    }

    /// <summary>
    /// Takes an object out of its room after a successful <see cref="CanPickUp"/>, freeing its tiles,
    /// as CorsixTH's pick up button does while a room is edited.
    /// </summary>
    internal static void PickUp(Simulation simulation, RoomObject placed)
    {
        simulation.RemoveEntity(placed.Id);
        if (simulation.FindEntity(placed.RoomId) is Room room)
        {
            Reoccupy(simulation, room);
        }
    }

    /// <summary>
    /// Recomputes which tiles of a room its objects take: the tiles go back to how they were before
    /// the room, the door's walkway is kept clear, and every remaining object occupies its footprint.
    /// Occupying only clears flags, so the order of the objects does not matter.
    /// </summary>
    private static void Reoccupy(Simulation simulation, Room room)
    {
        var map = simulation.Map;
        var area = room.Area;
        for (var y = area.Y; y < area.Bottom; y++)
        {
            for (var x = area.X; x < area.Right; x++)
            {
                ref var cell = ref map[x, y];
                cell.Flags = (cell.Flags & ~Room.RestorableFlags) | room.OriginalTileAt(new(x, y)).Flags;
                cell.ObjectId = 0;
            }
        }

        if (simulation.FindEntity(room.DoorId) is RoomDoor door)
        {
            var inside = door.InsideTile(area);
            map[inside.X, inside.Y].Set(TileFlags.Buildable, false);
        }

        foreach (var placed in simulation.Entities.OfType<RoomObject>())
        {
            if (placed.RoomId == room.Id
                && simulation.Content.FindObject(placed.ObjectType) is { } definition
                && definition.Orientations.TryGetValue(placed.Orientation, out var layout))
            {
                Occupy(map, placed.Tile, layout, placed.Id);
            }
        }
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

    /// <summary>
    /// Marks a side object on the map, as CorsixTH's <c>occupyTilesByObjectFootprintAt</c> does for an <c>only_side</c>
    /// tile: the side is no longer buildable, and an open edge is closed to walking.
    /// </summary>
    private static void OccupySide(LevelMap map, TilePosition tile, (Direction Side, int Dx, int Dy, TileFlags Buildable, FootprintFlags Needed) side, int objectId)
    {
        ref var cell = ref map[tile.X, tile.Y];
        cell.Set(side.Buildable, false);
        cell.ObjectId = objectId;
        if (!cell.Has(TravelFlag(side.Side)))
        {
            return;
        }

        var beyond = new TilePosition(tile.X + side.Dx, tile.Y + side.Dy);
        switch (side.Side)
        {
            case Direction.North:
                cell.Set(TileFlags.BlockedNorth, true);
                break;
            case Direction.West:
                cell.Set(TileFlags.BlockedWest, true);
                break;
            case Direction.South:
                map[beyond.X, beyond.Y].Set(TileFlags.BlockedNorth, true);
                break;
            default:
                map[beyond.X, beyond.Y].Set(TileFlags.BlockedWest, true);
                break;
        }

        cell.Set(TravelFlag(side.Side), false);
        map[beyond.X, beyond.Y].Set(TravelFlag((Direction)(((int)side.Side + 2) % 4)), false);
    }

    private static bool IsAllowedIn(Simulation simulation, ObjectDefinition definition, int roomId)
    {
        if (roomId == 0)
        {
            return definition.CorridorObject;
        }

        // Objects go into rooms while they are being built, as in CorsixTH's edit room dialog.
        return simulation.FindEntity(roomId) is Room { IsActive: false } room
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
