// Patients dropping litter, ported from CorsixTH CorsixTH/Lua/entities/humanoids/patient.lua (Patient:setTile) and the
// object search it uses (World:findObjectNear, CorsixTH/Src/th_pathfind.cpp object_visitor).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Pathfinding;

namespace CorsixNet.Sim.People;

/// <summary>
/// <c>Patient:setTile</c>'s litter: on their first hospital tile a patient gets a countdown of 20 to 100 tiles; when it
/// runs out on a free floor tile of their hospital outside rooms, they drop a can, banana, paper or bottle, unless a bin
/// is within 8 steps, when they do so only one time in 20. Then the countdown starts again, at 30 to 150.
/// </summary>
internal static class Littering
{
    /// <summary>How far away a bin keeps a patient from littering (<c>findObjectNear(self, "bin", 8)</c>).</summary>
    private const int BinDistance = 8;

    private const string BinType = "bin";

    /// <summary><c>Patient:setTile</c>: the patient steps onto <paramref name="tile"/>.</summary>
    internal static void OnTileEntered(Simulation simulation, Humanoid humanoid, PatientBehaviour patient, TilePosition tile)
    {
        var map = simulation.Map;
        if (!map.Contains(tile.X, tile.Y))
        {
            return;
        }

        if (patient.LitterCountdown < 0)
        {
            // Arrived at the first tile of the hospital: small hospitals are around 40 to 50 tiles.
            if (map[tile.X, tile.Y].Has(TileFlags.Buildable))
            {
                patient.LitterCountdown = simulation.Random.NextInclusive(20, 100);
            }

            return;
        }

        if (humanoid.HospitalPlayer < 0 || patient.Phase is PatientPhase.GoingHome or PatientPhase.Dying)
        {
            return;
        }

        patient.LitterCountdown--;
        if (patient.LitterCountdown != 0)
        {
            return;
        }

        var cell = map[tile.X, tile.Y];
        if (cell.RoomId == 0
            && !AnyObjectAt(simulation, tile)
            && cell.Has(TileFlags.Buildable)
            && Disasters.Epidemics.IsInHospital(simulation, humanoid.HospitalPlayer, tile)
            && (!IsBinNear(simulation, tile) || simulation.Random.Next(100) < 5))
        {
            var trash = (LitterType)simulation.Random.NextInclusive(1, 4);
            Litter.Drop(simulation, humanoid.HospitalPlayer, trash, tile, simulation.Random.NextInclusive(0, 1) == 1);
        }

        // Always give new litter to drop.
        patient.LitterCountdown = simulation.Random.NextInclusive(30, 150);
    }

    /// <summary>Whether anything stands on the tile, as CorsixTH's <c>World:getObjects</c> knows objects by their own tile.</summary>
    private static bool AnyObjectAt(Simulation simulation, TilePosition tile)
    {
        // A rat hole in a north or west wall is an object on its tile too.
        foreach (var hospital in simulation.Hospitals)
        {
            if (hospital.Ratholes.Any(hole => hole.IsVisible && hole.Tile == tile))
            {
                return true;
            }
        }

        foreach (var entity in simulation.Entities)
        {
            var at = entity switch
            {
                RoomObject placed => placed.Tile == tile,
                RoomDoor door => door.Tile == tile,
                EntranceDoor door => door.Tile == tile,
                Litter litter => !litter.IsRemoved && litter.Tile == tile,
                _ => false,
            };
            if (at)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>World:findObjectNear(self, "bin", 8)</c>, by CorsixTH's object visitor.</summary>
    private static bool IsBinNear(Simulation simulation, TilePosition start)
    {
        var bins = new HashSet<TilePosition>();
        foreach (var entity in simulation.Entities)
        {
            if (entity is RoomObject { ObjectType: BinType } bin)
            {
                bins.Add(bin.Tile);
            }
        }

        return bins.Count > 0 && ObjectVisitor.Visit(simulation.Map, start, BinDistance, bins.Contains);
    }
}
