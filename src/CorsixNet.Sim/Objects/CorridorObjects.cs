// Moving and selling objects standing in the corridor, ported from CorsixTH CorsixTH/Lua/entities/object.lua
// (Object:onClick's right click on a corridor_object, onPickUp, resetUsageAndReservaton), objects/reception_desk.lua
// (ReceptionDesk:onPickUp, resetUsageAndReservaton: queue:rerouteAllPatients) and dialogs/place_objects.lua
// (UIPlaceObjects:placeObject with an existing_object, removeObject's sale of one: build cost back).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;

namespace CorsixNet.Sim.Objects;

/// <summary>
/// The corridor's objects the player picks up (reception desks, benches, drinks machines, plants and the rest of
/// <c>corridor_object</c>): put down elsewhere, or sold for what they cost.
/// </summary>
public static class CorridorObjects
{
    /// <summary>
    /// Whether <paramref name="player"/> may pick the object up: a corridor object standing in the corridor of their
    /// hospital. (In CorsixTH those in a finished room can be picked up too, but they only go back into it while it is
    /// edited here.)
    /// </summary>
    public static bool CanPickUp(Simulation simulation, int objectId, int player) =>
        simulation.FindEntity(objectId) is RoomObject { RoomId: 0, IsDestroyed: false } placed
        && simulation.Content.FindObject(placed.ObjectType) is { CorridorObject: true } definition
        && definition.Orientations.TryGetValue(placed.Orientation, out var layout)
        && !layout.Footprint.Any(part => part.IsSide)
        && simulation.Map.Contains(placed.Tile.X, placed.Tile.Y)
        && simulation.Map.GetParcelOwner(simulation.Map[placed.Tile.X, placed.Tile.Y].Parcel) == player + 1;

    /// <summary>
    /// Whether the object could stand at <paramref name="tile"/> facing <paramref name="orientation"/> with itself out of
    /// the way (<see cref="ObjectPlacer.Check"/> on the map as it would be without it). The map is left as it was.
    /// </summary>
    public static ObjectPlacementProblem CheckMove(Simulation simulation, RoomObject placed, TilePosition tile, Direction orientation, int player)
    {
        var map = simulation.Map;
        const int Reach = 4;
        var saved = new List<(int X, int Y, MapTile Cell)>();
        for (var y = placed.Tile.Y - Reach; y <= placed.Tile.Y + Reach; y++)
        {
            for (var x = placed.Tile.X - Reach; x <= placed.Tile.X + Reach; x++)
            {
                if (map.Contains(x, y))
                {
                    saved.Add((x, y, map[x, y]));
                }
            }
        }

        ObjectPlacer.Vacate(simulation, placed);
        try
        {
            return ObjectPlacer.Check(simulation, placed.ObjectType, tile, orientation, player);
        }
        finally
        {
            foreach (var (x, y, cell) in saved)
            {
                map[x, y] = cell;
            }
        }
    }

    /// <summary>
    /// <c>UIPlaceObjects:placeObject</c> with the picked up object: if it may stand at <paramref name="tile"/> facing
    /// <paramref name="orientation"/>, it goes there; those using it were sent off, as the pick up does (<c>onPickUp</c>).
    /// A plant keeps how thirsty it is.
    /// </summary>
    /// <returns>Whether the object moved.</returns>
    internal static bool Move(Simulation simulation, RoomObject placed, TilePosition tile, Direction orientation, int player)
    {
        if (CheckMove(simulation, placed, tile, orientation, player) != ObjectPlacementProblem.None)
        {
            return false;
        }

        Remove(simulation, placed);
        var put = ObjectPlacer.Place(simulation, simulation.Content.FindObject(placed.ObjectType)!, tile, orientation);
        if (placed.Plant is { } plant)
        {
            put.Plant = plant;
        }

        return true;
    }

    /// <summary><c>removeObject</c> for a picked up object not put down again: it goes, and its build cost comes back.</summary>
    internal static void Sell(Simulation simulation, RoomObject placed, int player)
    {
        Remove(simulation, placed);
        Economy.RoomPurchases.SellObject(simulation, placed.ObjectType, player);
    }

    /// <summary>
    /// Takes the object off the map: its tiles are as they were before it (as the map file has them, with any other
    /// object's footprint on them again); patients queueing at a reception desk look for another (<c>rerouteAllPatients</c>);
    /// those on a bench or at a machine notice it has gone and carry on; queues are arranged again without a bench.
    /// </summary>
    internal static void Remove(Simulation simulation, RoomObject placed)
    {
        simulation.RemoveEntity(placed.Id);
        ObjectPlacer.Vacate(simulation, placed);
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Behaviour: PatientBehaviour patient } && patient.TargetId == placed.Id)
            {
                patient.LeaveRemovedDesk(placed.Id);
            }
        }

        if (string.Equals(placed.ObjectType, "bench", StringComparison.Ordinal))
        {
            RoomVisits.ArrangeAllQueues(simulation);
        }
    }
}
