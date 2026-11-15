// Picking up a member of staff and putting them down elsewhere, ported from CorsixTH
// CorsixTH/Lua/humanoid_actions/pickup.lua (action_pickup_start), CorsixTH/Lua/dialogs/place_staff.lua
// (UIPlaceStaff:onMouseUp, _isValidStaffPlacement) and CorsixTH/Lua/entities/humanoids/staff.lua (Staff:onPickup,
// Staff:onPlaceInCorridor).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;

namespace CorsixNet.Sim.Staff;

/// <summary>
/// Moving a member of staff with the hand cursor: they are picked up off their job and put down on a tile where a new
/// member of staff could be placed, where they start afresh.
/// </summary>
/// <remarks>
/// In CorsixTH picking up and putting down are two steps, with the member of staff invisible and doing nothing in
/// between. Here, as with hiring, the client shows the picking up and the move is one command when they are put down;
/// putting them back where they were is no command at all.
/// </remarks>
public static class StaffMoving
{
    /// <summary>Whether the player may move the member of staff <paramref name="staffId"/> to <paramref name="tile"/>.</summary>
    public static StaffHireProblem Check(Simulation simulation, int staffId, TilePosition tile, int player)
    {
        if (simulation.FindEntity(staffId) is not Humanoid { Profile: { } profile, IsDespawning: false, IsFired: false } staff
            || staff.Employer != player || staff.Behaviour is null or LeavingBehaviour)
        {
            return StaffHireProblem.NoSuchStaff;
        }

        // CorsixTH lets the room deal with a member of staff leaving in the middle of a treatment: here one using an
        // object with a patient stops (StaffEmployment.Detach) and the patient waits for the next. The operating
        // theatre's surgeons, whose table actions must happen, finish the operation first (see Move).

        return StaffHiring.CheckPlacement(simulation, profile.Class, tile, player, staffId);
    }

    /// <summary>
    /// Moves the member of staff after a successful <see cref="Check"/>: off their job (<c>action_pickup_start</c>,
    /// <c>room:onHumanoidLeave</c>), onto the tile (<c>setTile</c>), and looking for work afresh as if just hired
    /// (<c>Staff:onPlaceInCorridor</c>); a handyman keeps his parcel.
    /// </summary>
    internal static void Move(Simulation simulation, Humanoid staff, TilePosition tile)
    {
        if (IsOperating(simulation, staff))
        {
            // Staff:setPickup with the operation's actions still to happen: done once the operation is over.
            ((DoctorBehaviour)staff.Behaviour!).DeferMove(tile);
            return;
        }

        var parcel = staff.Behaviour is HandymanBehaviour handyman ? handyman.Parcel : 0;
        StaffEmployment.Detach(simulation, staff);
        simulation.NotifyOccupantChange(tile, 1);
        simulation.NotifyOccupantChange(staff.CountedTile, -1);
        staff.PlaceAt(tile);
        staff.Behaviour = StaffHiring.NewBehaviour(staff.Profile!.Class);
        if (staff.Behaviour is HandymanBehaviour placed)
        {
            placed.Parcel = parcel;
        }

        Tiredness.UpdateSpeed(simulation, staff);

        // Room:onHumanoidEnter, when put down in a room.
        RoomVisits.SettleIdleStaff(simulation, staff, placed: true);
    }

    /// <summary>Whether the member of staff is a surgeon in the middle of an operation.</summary>
    private static bool IsOperating(Simulation simulation, Humanoid staff) =>
        StaffEmployment.IsBusyWithPatient(simulation, staff) && staff.Behaviour is DoctorBehaviour surgeon
        && simulation.FindEntity(surgeon.RoomId) is Room { RoomType: RoomVisits.OperatingTheatreRoom };

    /// <summary>
    /// After an operation: a surgeon picked up during it is put down where the player put them, if they may still stand
    /// there; otherwise they stay.
    /// </summary>
    internal static void MovePending(Simulation simulation, Humanoid staff)
    {
        if (staff.Behaviour is not DoctorBehaviour { PendingMove: { } tile } surgeon)
        {
            return;
        }

        surgeon.ClearPendingMove();
        if (Check(simulation, staff.Id, tile, staff.Employer) == StaffHireProblem.None)
        {
            Move(simulation, staff, tile);
        }
    }
}
