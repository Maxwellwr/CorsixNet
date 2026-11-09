// Ported from CorsixTH CorsixTH/Src/th_pathfind.cpp (object_visitor), which World:findObjectNear uses.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Maps;

namespace CorsixNet.Sim.Pathfinding;

/// <summary>
/// CorsixTH's object visitor: it spreads from a tile over passable tiles, not through doors, up to a number of steps,
/// and offers each tile next to one it reaches (in the directions the map lets one travel) to look for objects there.
/// A tile can be offered more than once, from different neighbours.
/// </summary>
internal static class ObjectVisitor
{
    /// <summary>The neighbours in the order CorsixTH's pathfinder tries them: west, east, north, south.</summary>
    private static readonly (int Dx, int Dy, TileFlags Travel)[] Neighbours =
    [
        (-1, 0, TileFlags.CanTravelWest), (1, 0, TileFlags.CanTravelEast), (0, -1, TileFlags.CanTravelNorth), (0, 1, TileFlags.CanTravelSouth),
    ];

    /// <summary>
    /// Offers the tiles around those within <paramref name="maxDistance"/> steps of <paramref name="start"/> to
    /// <paramref name="visit"/>, which returns true to end the search.
    /// </summary>
    /// <returns>Whether <paramref name="visit"/> ended the search.</returns>
    internal static bool Visit(LevelMap map, TilePosition start, int maxDistance, Func<TilePosition, bool> visit)
    {
        // Every step costs the same, so a breadth-first search reaches the tiles in the visitor's order of distance.
        var distances = new Dictionary<TilePosition, int> { [start] = 0 };
        var open = new Queue<TilePosition>();
        open.Enqueue(start);
        while (open.Count > 0)
        {
            var node = open.Dequeue();
            var distance = distances[node];
            var flags = map[node.X, node.Y].Flags;
            foreach (var (dx, dy, travel) in Neighbours)
            {
                if ((flags & travel) == 0)
                {
                    continue;
                }

                var next = new TilePosition(node.X + dx, node.Y + dy);
                if (visit(next))
                {
                    return true;
                }

                var nextFlags = map[next.X, next.Y].Flags;
                var throughDoor = travel switch
                {
                    TileFlags.CanTravelNorth => (flags & TileFlags.DoorNorth) != 0,
                    TileFlags.CanTravelEast => (nextFlags & TileFlags.DoorWest) != 0,
                    TileFlags.CanTravelSouth => (nextFlags & TileFlags.DoorNorth) != 0,
                    _ => (flags & TileFlags.DoorWest) != 0,
                };
                if (distance < maxDistance && !throughDoor && (nextFlags & TileFlags.Passable) != 0 && distances.TryAdd(next, distance + 1))
                {
                    open.Enqueue(next);
                }
            }
        }

        return false;
    }
}
