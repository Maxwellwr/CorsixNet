// How staff tire, ported from CorsixTH CorsixTH/Lua/entities/humanoids/staff.lua (Staff:tick, tickDay, isTiring,
// tire, checkIfNeedRest, isVeryTired, isCrackUpTired, updateSpeed) and base_config.lua (gbv.Tired, VeryTired, CrackUpTired).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Staff;

/// <summary>
/// The level's tiredness thresholds in thousandths of fatigue: tired enough to want the staff room (the hospital's
/// <c>goto_staffroom</c> policy, <c>gbv.Tired</c>), very tired (<c>gbv.VeryTired</c>) and so tired they could crack up
/// (<c>gbv.CrackUpTired</c>).
/// </summary>
public sealed record TirednessRules(int Tired, int VeryTired, int CrackUpTired)
{
    /// <summary>CorsixTH's base_config.lua: 600, 700 and 800.</summary>
    public static readonly TirednessRules Default = new(600, 700, 800);

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(Tired);
        writer.WriteInt32(VeryTired);
        writer.WriteInt32(CrackUpTired);
    }

    internal static TirednessRules Read(SimReader reader) => new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
}

/// <summary>
/// Staff tire while they work: every hour in a room (other than resting in a staff room), or anywhere for a handyman,
/// adds <see cref="TirePerHour"/> to their fatigue and costs a little happiness. Tired staff work worse and grow
/// unhappy, all the more without a staff room to rest in.
/// </summary>
/// <remarks>
/// A member of staff on call (CorsixTH's <c>on_call</c>: answering one of the dispatcher's calls, such as a handyman sent
/// to sweep or water in the staff room) neither rests nor stops tiring there. Here a handyman is on call while he has a
/// job (anything but meandering or leaving a room once done), a nurse while she goes to vaccinate, and a doctor or nurse
/// while walking to the room that sent for them.
/// </remarks>
internal static class Tiredness
{
    /// <summary>What an hour of work adds to fatigue (<c>self:tire(0.000090)</c>).</summary>
    internal const decimal TirePerHour = 0.000090m;

    /// <summary>The staff room's room type.</summary>
    internal const string StaffRoomType = "staff_room";

    private const string TrainingRoomType = "training";

    /// <summary>One hour of tiring (<c>Staff:tick</c>'s <c>isTiring</c> and <c>tire</c>).</summary>
    internal static void TireIfWorking(Simulation simulation, Humanoid humanoid)
    {
        if (IsTiring(simulation, humanoid))
        {
            // Staff:tire: the no rest cheat keeps the hospital's staff fresh, but not happy.
            if (!HasNoRestCheat(simulation, humanoid))
            {
                humanoid.Fatigue = Math.Min(humanoid.Fatigue + TirePerHour, 1m);
            }

            Feelings.ChangeHappiness(simulation, humanoid, -0.00002m);
        }
    }

    /// <summary>The fatigue part of <c>Staff:tickDay</c>, for staff other than receptionists.</summary>
    internal static void OnEndDay(Simulation simulation, Humanoid humanoid)
    {
        var fatigue = humanoid.Fatigue;
        if (IsVeryTired(simulation, fatigue) || !IsResting(simulation, humanoid))
        {
            // Working when you should be taking a break makes you unhappy.
            if (fatigue >= GoToStaffRoom(simulation, humanoid))
            {
                Feelings.ChangeHappiness(simulation, humanoid, -0.02m);
            }

            // So does getting tired.
            if (fatigue >= 0.5m)
            {
                Feelings.ChangeHappiness(simulation, humanoid, -0.01m);
            }
        }
        else
        {
            // Resting and no longer very tired: things can only get better.
            humanoid.IsTiredMoodShown = false;
            Feelings.ChangeHappiness(simulation, humanoid, 0.006m);
        }
    }

    /// <summary>
    /// <c>Doctor:tickDay</c>'s overwork: a very tired doctor may go crazy (one day in 300); one who is no longer very
    /// tired comes to their senses while resting (<c>Doctor:setCrazy</c>).
    /// </summary>
    internal static void CheckCrazy(Simulation simulation, Humanoid humanoid)
    {
        if (!IsVeryTired(simulation, humanoid.Fatigue))
        {
            if (humanoid.IsCrazy && IsResting(simulation, humanoid) && humanoid.Layers[Humanoid.StaffLayer] >= 5)
            {
                humanoid.SetLayer(Humanoid.StaffLayer, (byte)(humanoid.Layers[Humanoid.StaffLayer] - 4));
                humanoid.IsCrazy = false;
            }
        }
        else if (simulation.Random.NextInclusive(1, 300) == 1 && !humanoid.IsCrazy && humanoid.Profile is { } profile)
        {
            humanoid.SetLayer(Humanoid.StaffLayer, (byte)(profile.Layer5 + 4));
            humanoid.IsCrazy = true;
        }
    }

    /// <summary>
    /// <c>Staff:updateSpeed</c>, at the end of every tick: juniors walk slowly and consultants fast; very tired staff
    /// one step slower and those tired enough to crack up two (receptionists never); a doctor in the training room
    /// slowly; with the no-rest cheat everyone fast.
    /// </summary>
    internal static void UpdateSpeed(Simulation simulation, Humanoid humanoid)
    {
        var profile = humanoid.Profile!;
        var level = profile.IsJunior ? 1 : profile.IsConsultant ? 3 : 2;
        if (RoomOf(simulation, humanoid) is { RoomType: TrainingRoomType } && profile.Class == StaffClass.Doctor)
        {
            level = 1;
        }
        else if (HasNoRestCheat(simulation, humanoid))
        {
            level = 3;
        }
        else if (profile.Class != StaffClass.Receptionist)
        {
            if (IsCrackUpTired(simulation, humanoid.Fatigue))
            {
                level -= 2;
            }
            else if (IsVeryTired(simulation, humanoid.Fatigue))
            {
                level--;
            }
        }

        humanoid.IsFast = level >= 3;
        humanoid.IsSlow = level <= 1;
    }

    private static bool HasNoRestCheat(Simulation simulation, Humanoid humanoid) =>
        humanoid.Employer >= 0
        && humanoid.Employer < simulation.Hospitals.Count
        && (simulation.Hospitals[humanoid.Employer].ActiveCheats & ToggleCheats.NoRest) != 0;

    /// <summary><c>Staff:isVeryTired</c>.</summary>
    internal static bool IsVeryTired(Simulation simulation, decimal fatigue) => fatigue * 1000 >= simulation.Rules.Tiredness.VeryTired;

    /// <summary><c>Staff:isCrackUpTired</c>.</summary>
    internal static bool IsCrackUpTired(Simulation simulation, decimal fatigue) => fatigue * 1000 >= simulation.Rules.Tiredness.CrackUpTired;

    /// <summary>A staff room was built: nobody waits for one any longer (<c>Staff:notifyNewRoom</c>).</summary>
    internal static void OnRoomActivated(Simulation simulation, Room room)
    {
        if (room.RoomType != StaffRoomType)
        {
            return;
        }

        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Profile: not null } humanoid)
            {
                humanoid.WaitingForStaffRoom = false;
            }
        }
    }

    /// <summary>The employer's <c>goto_staffroom</c> policy, which starts at the level's <c>gbv.Tired</c>.</summary>
    private static decimal GoToStaffRoom(Simulation simulation, Humanoid humanoid) =>
        humanoid.Employer >= 0 && humanoid.Employer < simulation.Hospitals.Count
            ? simulation.Hospitals[humanoid.Employer].Policies.GoToStaffRoom / 1000m
            : simulation.Rules.Tiredness.Tired / 1000m;

    /// <summary>
    /// <c>Staff:checkIfNeedRest</c>: very tired staff lose happiness; tired staff outside a staff room want one, and
    /// lose happiness every hour while their hospital has none.
    /// </summary>
    internal static void CheckIfNeedRest(Simulation simulation, Humanoid humanoid)
    {
        // Only when very tired does the icon show.
        if (IsVeryTired(simulation, humanoid.Fatigue))
        {
            humanoid.IsTiredMoodShown = true;
            Feelings.ChangeHappiness(simulation, humanoid, -0.0002m);
        }

        WantStaffRoom(simulation, humanoid);
    }

    /// <summary>The rest of <c>checkIfNeedRest</c>: a tired member of staff outside a staff room goes to one if they may.</summary>
    internal static void WantStaffRoom(Simulation simulation, Humanoid humanoid)
    {
        if (humanoid.Fatigue < GoToStaffRoom(simulation, humanoid) || RoomOf(simulation, humanoid) is { RoomType: StaffRoomType })
        {
            return;
        }

        if (humanoid.WaitingForStaffRoom)
        {
            Feelings.ChangeHappiness(simulation, humanoid, -0.001m);
            return;
        }

        if (!HasStaffRoom(simulation, humanoid.Employer))
        {
            humanoid.WaitingForStaffRoom = true;
            return;
        }

        if (humanoid.Behaviour is RestingBehaviour || !MayLeaveWork(simulation, humanoid) || NearestStaffRoom(simulation, humanoid) is not { } staffRoom)
        {
            // staffroom_needed: they go once the patient has left (or the handyman has finished the job).
            return;
        }

        // Staff:goToStaffRoom: out of the room (Room:createLeaveAction) and to the nearest staff room.
        var parcel = humanoid.Behaviour is HandymanBehaviour handyman ? handyman.Parcel : 0;

        // last_room: a researcher or a doctor in training goes back there once rested, if it still has room for them.
        var returnRoom = humanoid.Behaviour is DoctorBehaviour doctor && simulation.FindEntity(doctor.RoomId) is Room { RoomType: RoomVisits.ResearchRoom or Training.RoomType } working
            ? working.Id
            : 0;
        // A surgeon changes back into a doctor at the theatre's screen first (operating_theatre.lua's must-happen actions).
        if (humanoid.Behaviour is DoctorBehaviour { IsChangingBack: true }
            || (humanoid.Type == HumanoidType.Surgeon && humanoid.Behaviour is DoctorBehaviour surgeon && surgeon.ChangeBackBeforeRest(simulation, humanoid)))
        {
            return;
        }

        StaffEmployment.Detach(simulation, humanoid);
        if (humanoid.Type == HumanoidType.Surgeon)
        {
            humanoid.Type = HumanoidType.Doctor;
        }

        humanoid.Behaviour = new RestingBehaviour(staffRoom.Id, parcel, returnRoom);
    }

    /// <summary>
    /// Whether the member of staff may leave what they are doing for the staff room: nurses may leave a ward with
    /// patients in its beds; others wait until the room's patient has gone (<c>may_leave</c>); a handyman finishes his
    /// job, a nurse her vaccination; receptionists never go.
    /// </summary>
    private static bool MayLeaveWork(Simulation simulation, Humanoid humanoid)
    {
        var roomId = humanoid.Behaviour switch
        {
            DoctorBehaviour doctor => doctor.RoomId,
            NurseBehaviour { VaccinatePatientId: 0 } nurse => nurse.RoomId,
            HandymanBehaviour { Phase: HandymanPhase.Meandering } => 0,
            _ => -1,
        };
        if (roomId < 0)
        {
            return false;
        }

        return simulation.FindEntity(roomId) is not Room room || room.RoomType == RoomVisits.WardRoom || room.PatientId == 0;
    }

    /// <summary><c>World:findRoomNear(self, "staff_room")</c>: the nearest working staff room of their hospital.</summary>
    private static Room? NearestStaffRoom(Simulation simulation, Humanoid humanoid)
    {
        Room? best = null;
        var bestDistance = 0;
        foreach (var entity in simulation.Entities)
        {
            if (entity is Room { IsActive: true, RoomType: StaffRoomType } room && RoomVisits.Owner(simulation, room) == humanoid.Employer
                && simulation.FindEntity(room.DoorId) is RoomDoor door
                && simulation.Pathfinder.FindDistance(simulation.Map, humanoid.StepTarget, door.InsideTile(room.Area)) is { } distance
                && (best is null || distance < bestDistance))
            {
                best = room;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// <c>Staff:isTiring</c>: in a room, unless resting in a staff room; outside rooms only a handyman tires.
    /// </summary>
    private static bool IsTiring(Simulation simulation, Humanoid humanoid)
    {
        if (RoomOf(simulation, humanoid) is { } room)
        {
            // Being in a staff room is quite refreshing, unless there on call.
            return room.RoomType != StaffRoomType || IsOnCall(simulation, humanoid);
        }

        return humanoid.Profile!.Class == StaffClass.Handyman;
    }

    /// <summary><c>Staff:isResting</c>: in a staff room and not on call.</summary>
    private static bool IsResting(Simulation simulation, Humanoid humanoid) =>
        RoomOf(simulation, humanoid) is { RoomType: StaffRoomType } && !IsOnCall(simulation, humanoid);

    /// <summary>
    /// Whether the member of staff answers a call (<c>on_call</c>): a handyman with a job, a nurse going to vaccinate,
    /// a doctor or nurse on the way to the room that sent for them.
    /// </summary>
    internal static bool IsOnCall(Simulation simulation, Humanoid humanoid) => humanoid.Behaviour switch
    {
        HandymanBehaviour handyman => handyman.Phase is not (HandymanPhase.Meandering or HandymanPhase.LeavingRoom),
        NurseBehaviour { VaccinatePatientId: not 0 } => true,
        NurseBehaviour { Phase: NursePhase.WalkingToRoom } => true,
        DoctorBehaviour { Phase: DoctorPhase.WalkingToRoom } => true,
        _ => false,
    };

    /// <summary><c>Humanoid:getRoom</c>: the room, built or not, whose floor the person stands on.</summary>
    private static Room? RoomOf(Simulation simulation, Humanoid humanoid)
    {
        var roomId = simulation.Map[humanoid.Tile.X, humanoid.Tile.Y].RoomId;
        return roomId != 0 ? simulation.FindEntity(roomId) as Room : null;
    }

    /// <summary>
    /// Whether the hospital has a working staff room (<c>World:findRoomNear(self, "staff_room")</c>, which looks for any
    /// active staff room that is not being edited).
    /// </summary>
    private static bool HasStaffRoom(Simulation simulation, int player)
    {
        foreach (var entity in simulation.Entities)
        {
            if (entity is Room { IsActive: true, RoomType: StaffRoomType } room && RoomVisits.Owner(simulation, room) == player)
            {
                return true;
            }
        }

        return false;
    }
}
