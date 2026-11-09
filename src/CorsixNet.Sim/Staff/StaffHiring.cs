// Where new staff may be placed and what hiring costs, ported from CorsixTH
// CorsixTH/Lua/dialogs/place_staff.lua (UIPlaceStaff:_isValidStaffPlacement, UIPlaceStaff:onMouseUp),
// CorsixTH/Lua/dialogs/hire_staff.lua (UIHireStaff:hire) and CorsixTH/Lua/hospital.lua (Hospital:addStaff).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;

namespace CorsixNet.Sim.Staff;

/// <summary>Why a candidate cannot be hired.</summary>
public enum StaffHireProblem
{
    None,
    NoSuchCandidate,
    NoHospital,
    CannotAfford,
    OutsideMap,
    NotInHospital,
    NotPassable,
    NoRoomsForReceptionists,
    DoorUnreachable,
}

/// <summary>
/// Hiring staff as the original does: the hospital needs a month's salary in the bank, the new member
/// of staff is placed on a tile of the player's hospital, and hiring costs one month's salary.
/// </summary>
public static class StaffHiring
{
    /// <summary>
    /// Checks hiring candidate <paramref name="index"/> of <paramref name="staffClass"/> for
    /// <paramref name="player"/> (0-based) and placing them on <paramref name="tile"/>.
    /// </summary>
    public static StaffHireProblem Check(Simulation simulation, StaffClass staffClass, int index, TilePosition tile, int player)
    {
        var pool = simulation.AvailableStaff(staffClass);
        if (index < 0 || index >= pool.Count)
        {
            return StaffHireProblem.NoSuchCandidate;
        }

        if (player < 0 || player >= simulation.Hospitals.Count)
        {
            return StaffHireProblem.NoHospital;
        }

        // UIHireStaff:hire refuses when the balance is below the wage.
        if (simulation.Hospitals[player].Balance < pool[index].Wage)
        {
            return StaffHireProblem.CannotAfford;
        }

        return CheckPlacement(simulation, staffClass, tile, player);
    }

    /// <summary>
    /// Whether a member of staff of <paramref name="staffClass"/> may be placed on <paramref name="tile"/>
    /// for <paramref name="player"/> (0-based), as CorsixTH's <c>_isValidStaffPlacement</c> decides:
    /// a passable tile inside the player's hospital, and in a room only if the room's door can be
    /// reached from it. Receptionists have no door animations and never go into rooms, but may be put
    /// straight onto a reception desk without a receptionist, which they then staff.
    /// </summary>
    public static StaffHireProblem CheckPlacement(Simulation simulation, StaffClass staffClass, TilePosition tile, int player)
    {
        var map = simulation.Map;
        if (!map.Contains(tile.X, tile.Y))
        {
            return StaffHireProblem.OutsideMap;
        }

        var cell = map[tile.X, tile.Y];
        if (map.GetParcelOwner(cell.Parcel) != player + 1 || !cell.Has(TileFlags.Hospital))
        {
            return StaffHireProblem.NotInHospital;
        }

        if (staffClass == StaffClass.Receptionist && UnstaffedDeskAt(simulation, tile) is not null)
        {
            return StaffHireProblem.None;
        }

        if (!cell.Has(TileFlags.Passable))
        {
            return StaffHireProblem.NotPassable;
        }

        if (cell.RoomId == 0)
        {
            return StaffHireProblem.None;
        }

        if (staffClass == StaffClass.Receptionist)
        {
            return StaffHireProblem.NoRoomsForReceptionists;
        }

        if (simulation.FindEntity(cell.RoomId) is not Room room || simulation.FindEntity(room.DoorId) is not RoomDoor door
            || !simulation.Pathfinder.TryFindPath(map, tile, door.Tile, []))
        {
            return StaffHireProblem.DoorUnreachable;
        }

        return StaffHireProblem.None;
    }

    /// <summary>
    /// A reception desk covering <paramref name="tile"/> without a receptionist at it (CorsixTH's
    /// <c>getObject(x, y, "reception_desk", true)</c> and <c>not desk.receptionist</c>), or null.
    /// </summary>
    public static RoomObject? UnstaffedDeskAt(Simulation simulation, TilePosition tile)
    {
        foreach (var desk in simulation.Entities.OfType<RoomObject>())
        {
            if (desk.Desk is not { ReceptionistId: 0 }
                || simulation.Content.FindObject(desk.ObjectType) is not { } definition
                || !definition.Orientations.TryGetValue(desk.Orientation, out var layout))
            {
                continue;
            }

            foreach (var part in layout.Footprint)
            {
                if (!part.IsUsePosition && !part.IsSide && desk.Tile.X + part.X == tile.X && desk.Tile.Y + part.Y == tile.Y)
                {
                    return desk;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Hires the candidate after a successful <see cref="Check"/>: takes them off the list, places them
    /// and pays their first month's salary.
    /// </summary>
    internal static Humanoid Hire(Simulation simulation, StaffClass staffClass, int index, TilePosition tile, int player)
    {
        var profile = simulation.TakeAvailableStaff(staffClass, index);
        var staff = simulation.AddEntity(id => Humanoid.CreateStaff(id, tile, profile, player));
        staff.Behaviour = staffClass switch
        {
            StaffClass.Doctor => new DoctorBehaviour(),
            StaffClass.Nurse => new NurseBehaviour(),
            StaffClass.Receptionist => new ReceptionistBehaviour(),
            StaffClass.Handyman => new HandymanBehaviour(),
            _ => null,
        };

        // Staff:onPlaceInCorridor.
        Tiredness.UpdateSpeed(simulation, staff);
        simulation.Hospitals[player].Spend(simulation.Hours, profile.Wage, TransactionKind.HireStaff, staffClass.ToString());
        return staff;
    }
}
