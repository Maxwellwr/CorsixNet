// Picking up, moving and selling corridor objects, ported from CorsixTH CorsixTH/Lua/entities/object.lua
// (Object:onClick's right click on a corridor_object, in the corridor or in a room not being edited; onPickUp and
// resetUsageAndReservaton: nobody uses a picked up object; the object kept invisible while picked_up),
// objects/reception_desk.lua (ReceptionDesk:onPickUp, resetUsageAndReservaton: queue:rerouteAllPatients) and
// dialogs/place_objects.lua (UIPlaceObjects:placeObject with an existing_object; removeObject: a cancelled move puts
// it back, a sale gives its build cost back).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.People;
using OpenWards.Sim.Rooms;

namespace OpenWards.Sim.Objects;

/// <summary>An object a hospital has picked up and not yet put down: what it is, where it stood, and a plant's thirst.</summary>
public sealed record CarriedObject(string ObjectType, TilePosition Tile, Direction Orientation, PlantState? Plant);

/// <summary>
/// The corridor objects the player picks up (reception desks, benches, drinks machines, plants, radiators and the rest
/// of <c>corridor_object</c>): carried while nobody uses them, then put down elsewhere, put back, or sold.
/// </summary>
public static class CorridorObjects
{
    /// <summary>
    /// Whether <paramref name="player"/> may pick the object up (<c>Object:onClick</c>): a corridor object of their
    /// hospital, in the corridor or in a finished room that is not being edited, while they carry nothing else.
    /// </summary>
    public static bool CanPickUp(Simulation simulation, int objectId, int player) =>
        simulation.FindEntity(objectId) is RoomObject { IsDestroyed: false } placed
        && simulation.Content.FindObject(placed.ObjectType) is { CorridorObject: true }
        && (placed.RoomId == 0 || simulation.FindEntity(placed.RoomId) is Room { IsActive: true, IsClosing: false, IsCrashed: false })
        && simulation.Map.Contains(placed.Tile.X, placed.Tile.Y)
        && simulation.Map.GetParcelOwner(simulation.Map[placed.Tile.X, placed.Tile.Y].Parcel) == player + 1
        && player >= 0 && player < simulation.Hospitals.Count && simulation.Hospitals[player].Carried is null;

    /// <summary>
    /// Whether the object could stand at <paramref name="tile"/> facing <paramref name="orientation"/> with itself out of
    /// the way (<see cref="ObjectPlacer.Check"/> on the map as it would be without it). The map is left as it was.
    /// </summary>
    public static ObjectPlacementProblem CheckMove(Simulation simulation, RoomObject placed, TilePosition tile, Direction orientation, int player)
    {
        var map = simulation.Map;
        var area = simulation.FindEntity(placed.RoomId) is Room room
            ? new TileRect(room.Area.X - 1, room.Area.Y - 1, room.Area.Width + 2, room.Area.Height + 2)
            : new TileRect(placed.Tile.X - 4, placed.Tile.Y - 4, 9, 9);
        var saved = new List<(int X, int Y, MapTile Cell)>();
        for (var y = area.Y; y < area.Bottom; y++)
        {
            for (var x = area.X; x < area.Right; x++)
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
    /// The right click: the object comes off the map and the hospital carries it; those using it, or on their way to it,
    /// are sent off (<c>onPickUp</c>), so nobody uses it until it is put down.
    /// </summary>
    internal static void PickUp(Simulation simulation, RoomObject placed, int player)
    {
        simulation.Hospitals[player].Carried = new CarriedObject(placed.ObjectType, placed.Tile, placed.Orientation, placed.Plant);
        Remove(simulation, placed);
    }

    /// <summary><c>UIPlaceObjects:placeObject</c> with the carried object: put down there if it may stand there.</summary>
    /// <returns>Whether it was put down.</returns>
    internal static bool PutDown(Simulation simulation, int player, TilePosition tile, Direction orientation)
    {
        if (simulation.Hospitals[player].Carried is not { } carried
            || ObjectPlacer.Check(simulation, carried.ObjectType, tile, orientation, player) != ObjectPlacementProblem.None)
        {
            return false;
        }

        Put(simulation, player, carried, tile, orientation);
        return true;
    }

    /// <summary>
    /// <c>UIPlaceObjects:undo</c> (a cancelled move): the carried object goes back where it stood; if something has taken
    /// its place meanwhile, it is sold instead.
    /// </summary>
    internal static void PutBack(Simulation simulation, int player)
    {
        if (simulation.Hospitals[player].Carried is not { } carried)
        {
            return;
        }

        if (ObjectPlacer.Check(simulation, carried.ObjectType, carried.Tile, carried.Orientation, player) == ObjectPlacementProblem.None)
        {
            Put(simulation, player, carried, carried.Tile, carried.Orientation);
        }
        else
        {
            SellCarried(simulation, player);
        }
    }

    /// <summary><c>UIPlaceObjects:sell</c>: the carried object is sold for its build cost.</summary>
    internal static void SellCarried(Simulation simulation, int player)
    {
        if (simulation.Hospitals[player].Carried is not { } carried)
        {
            return;
        }

        simulation.Hospitals[player].Carried = null;
        Economy.RoomPurchases.SellObject(simulation, carried.ObjectType, player);
    }

    private static void Put(Simulation simulation, int player, CarriedObject carried, TilePosition tile, Direction orientation)
    {
        simulation.Hospitals[player].Carried = null;
        var put = ObjectPlacer.Place(simulation, simulation.Content.FindObject(carried.ObjectType)!, tile, orientation);
        if (carried.Plant is { } plant)
        {
            put.Plant = plant;
        }

        simulation.Map.UpdatePathfinding();
    }

    /// <summary>
    /// The object moves in one go, if it may stand at <paramref name="tile"/> with itself out of the way (the move command
    /// of an earlier version; the client now picks up, carries and puts down).
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

        simulation.Map.UpdatePathfinding();
        return true;
    }

    /// <summary><c>removeObject</c> for a picked up object not put down again: it goes, and its build cost comes back.</summary>
    internal static void Sell(Simulation simulation, RoomObject placed, int player)
    {
        Remove(simulation, placed);
        Economy.RoomPurchases.SellObject(simulation, placed.ObjectType, player);
    }

    /// <summary>
    /// Takes the object off the map: its tiles are as they were before it (as the map file or its room has them, with any
    /// other object's footprint on them again); patients queueing at a reception desk look for another
    /// (<c>rerouteAllPatients</c>); those on a bench or at a machine notice it has gone and carry on; queues are arranged
    /// again without a bench.
    /// </summary>
    internal static void Remove(Simulation simulation, RoomObject placed)
    {
        simulation.RemoveEntity(placed.Id);
        ObjectPlacer.Vacate(simulation, placed);
        simulation.Map.UpdatePathfinding();
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
