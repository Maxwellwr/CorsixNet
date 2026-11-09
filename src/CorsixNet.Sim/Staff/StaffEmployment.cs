// Firing staff and staff asking for raises, ported from CorsixTH CorsixTH/Lua/entities/humanoids/staff.lua
// (Staff:tick, checkIfWaitedTooLongForRaise, requestRaise, increaseWage, fire, despawn), staff_profile.lua
// (StaffProfile:getRiseAmount) and dialogs/staff_rise.lua (UIStaffRise:increaseSalary, fireStaff).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;

namespace CorsixNet.Sim.Staff;

/// <summary>
/// Staff who are very unhappy (below 0.1) for <see cref="RaiseDelay"/> hours ask for a raise, one time in five; if the
/// player does not answer within about 15 days they get it (with the grant wage increase policy) or quit. Fired staff
/// get a month's wage, cost reputation and walk off the map.
/// </summary>
internal static class StaffEmployment
{
    /// <summary>Hours of unhappiness before a raise is asked for (<c>timer_until_raise</c>).</summary>
    internal const int RaiseDelay = 200;

    /// <summary>Hours a raise request waits for an answer, plus up to <see cref="QuittingSpread"/> more (<c>25*30</c>).</summary>
    internal const int QuittingDelay = 25 * 30;

    internal const int QuittingSpread = 50;

    /// <summary>Below this happiness staff think of a raise.</summary>
    internal const decimal UnhappyForRaise = 0.1m;

    /// <summary>One hour of a member of staff (<c>Staff:tick</c>, the checks after <c>Entity.tick</c>).</summary>
    internal static void Tick(Simulation simulation, Humanoid humanoid)
    {
        Tiredness.CheckIfNeedRest(simulation, humanoid);
        if (CheckIfWaitedTooLongForRaise(simulation, humanoid))
        {
            return;
        }

        Tiredness.TireIfWorking(simulation, humanoid);

        // Very unhappy staff ask for a raise after a while, except in free build mode (World:World's
        // debug_disable_salary_raise).
        if (humanoid.Happiness < UnhappyForRaise && !simulation.Rules.FreeBuild)
        {
            if (humanoid.RaiseTimer < 0)
            {
                humanoid.RaiseTimer = RaiseDelay;
            }

            if (humanoid.RaiseTimer == 0)
            {
                RequestRaise(simulation, humanoid);
            }
            else
            {
                humanoid.RaiseTimer--;
            }
        }
        else
        {
            humanoid.RaiseTimer = -1;
        }

        Tiredness.UpdateSpeed(simulation, humanoid);
    }

    /// <summary><c>StaffProfile:getRiseAmount</c>: a tenth more, or half-way to the fair wage if that is more.</summary>
    internal static int RiseAmount(Simulation simulation, StaffProfile profile) =>
        (int)decimal.Floor(Math.Max(profile.Wage * 1.1m, (profile.FairWage(simulation.Rules.Staff) + profile.Wage) / 2m) - profile.Wage);

    /// <summary>
    /// The player's answer to a raise request (<c>UIStaffRise</c>): granting it raises the wage, refusing it fires the
    /// member of staff.
    /// </summary>
    internal static void AnswerRaise(Simulation simulation, Humanoid humanoid, bool grant)
    {
        if (humanoid.RaiseRequest <= 0)
        {
            return;
        }

        if (grant)
        {
            IncreaseWage(simulation, humanoid, humanoid.RaiseRequest);
            humanoid.QuittingIn = -1;
        }
        else
        {
            Fire(simulation, humanoid);
        }
    }

    /// <summary>
    /// <c>Staff:fire</c>: a month's wage as severance, a little reputation, and the member of staff leaves through a
    /// spawn point (<c>despawn</c>), no longer counting as staff.
    /// </summary>
    internal static void Fire(Simulation simulation, Humanoid humanoid)
    {
        if (humanoid.IsFired || humanoid.Profile is not { } profile || humanoid.Employer < 0 || humanoid.Employer >= simulation.Hospitals.Count)
        {
            return;
        }

        var hospital = simulation.Hospitals[humanoid.Employer];
        hospital.Spend(simulation.Hours, profile.Wage, TransactionKind.Severance, profile.Class.ToString());
        humanoid.IsFired = true;
        humanoid.RaiseRequest = 0;
        humanoid.RaiseTimer = -1;
        humanoid.QuittingIn = -1;
        hospital.ChangeReputation(simulation.Random, ReputationChange.Kicked);
        LeaveWhenFree(simulation, humanoid);
    }

    /// <summary>
    /// A fired member of staff leaves, unless they are in the middle of using an object together with a patient (the
    /// pharmacy's drink, the crash trolley's examination); then they finish that first.
    /// </summary>
    internal static void LeaveWhenFree(Simulation simulation, Humanoid humanoid)
    {
        if (!IsBusyWithPatient(simulation, humanoid))
        {
            Leave(simulation, humanoid);
        }
    }

    private static bool IsBusyWithPatient(Simulation simulation, Humanoid humanoid) => humanoid.Behaviour switch
    {
        NurseBehaviour { Phase: NursePhase.Treating } => true,
        DoctorBehaviour doctor => simulation.FindEntity(doctor.RoomId) is Room room
            && simulation.FindEntity(room.PatientId) is Humanoid { Behaviour: PatientBehaviour { TrolleyUse: not null } },
        _ => false,
    };

    /// <summary>
    /// <c>Staff:checkIfWaitedTooLongForRaise</c>: when a request has waited too long, the raise is granted under the
    /// grant wage increase policy, otherwise the member of staff is fired.
    /// </summary>
    /// <returns>Whether the member of staff was fired.</returns>
    private static bool CheckIfWaitedTooLongForRaise(Simulation simulation, Humanoid humanoid)
    {
        if (humanoid.QuittingIn < 0)
        {
            return false;
        }

        humanoid.QuittingIn--;
        if (humanoid.QuittingIn >= 0)
        {
            return false;
        }

        if (simulation.Hospitals[humanoid.Employer].Policies.GrantWageIncrease)
        {
            IncreaseWage(simulation, humanoid, RiseAmount(simulation, humanoid.Profile!));
            return false;
        }

        Fire(simulation, humanoid);
        return true;
    }

    /// <summary>
    /// <c>Staff:requestRaise</c>: at the highest salary nobody asks; otherwise, without a request pending, one time in
    /// five the member of staff asks for <see cref="RiseAmount"/>, at most up to the highest salary.
    /// </summary>
    private static void RequestRaise(Simulation simulation, Humanoid humanoid)
    {
        var profile = humanoid.Profile!;
        var maxSalary = simulation.Rules.MaxSalary;
        if (profile.Wage >= maxSalary)
        {
            humanoid.RaiseTimer = -1;
            return;
        }

        if (humanoid.RaiseRequest > 0)
        {
            return;
        }

        var amount = RiseAmount(simulation, profile);
        if (profile.Wage + amount > maxSalary)
        {
            amount = maxSalary - profile.Wage;
        }

        // Staff are timid: they only ask one time in five.
        if (simulation.Random.NextInclusive(1, 5) != 1 || amount <= 0)
        {
            humanoid.RaiseTimer = -1;
            return;
        }

        humanoid.RaiseRequest = amount;
        humanoid.QuittingIn = QuittingDelay + simulation.Random.NextInclusive(0, QuittingSpread);
    }

    /// <summary><c>Staff:increaseWage</c>: up to the highest salary, and the member of staff is happy again.</summary>
    private static void IncreaseWage(Simulation simulation, Humanoid humanoid, int amount)
    {
        var profile = humanoid.Profile!;
        var maxSalary = simulation.Rules.MaxSalary;
        if (profile.Wage < maxSalary)
        {
            profile.Wage = Math.Min(profile.Wage + amount, maxSalary);
        }

        humanoid.RaiseRequest = 0;
        Feelings.ChangeHappiness(simulation, humanoid, 0.99m);
    }

    /// <summary>
    /// Leaves the job at once: gives up the room or desk (which then sends for someone else), steps off any object and
    /// walks to a random spawn point (<c>Humanoid:despawn</c>).
    /// </summary>
    private static void Leave(Simulation simulation, Humanoid humanoid)
    {
        switch (humanoid.Behaviour)
        {
            case DoctorBehaviour doctor:
                LeaveRoom(simulation, humanoid, doctor.RoomId, doctor.Use?.OldTile);
                break;
            case NurseBehaviour nurse:
                LeaveRoom(simulation, humanoid, nurse.RoomId, null);
                break;
            case ReceptionistBehaviour receptionist when simulation.FindEntity(receptionist.DeskId) is RoomObject { Desk: { } state } desk:
                if (state.ReceptionistId == humanoid.Id)
                {
                    state.ReceptionistId = 0;
                }

                if (state.ReservedForId == humanoid.Id)
                {
                    state.ReservedForId = 0;
                }

                if (humanoid.Tile == desk.Tile)
                {
                    humanoid.PlaceAt(ReceptionDesks.SecondaryUseTile(simulation, desk));
                }

                break;
        }

        humanoid.ClearActionAnimation();
        humanoid.IsHidden = false;
        humanoid.StopWalking();
        var points = simulation.Arrivals.SpawnPointsOf(simulation.Map);
        var home = points.Count > 0 ? points[simulation.Random.Next(points.Count)] : humanoid.Tile;
        humanoid.Behaviour = new LeavingBehaviour(home);
    }

    private static void LeaveRoom(Simulation simulation, Humanoid humanoid, int roomId, Maps.TilePosition? standAt)
    {
        if (simulation.FindEntity(roomId) is Room room && room.StaffId == humanoid.Id)
        {
            room.StaffId = 0;
            room.StaffPresent = false;
        }

        if (standAt is { } tile && tile != humanoid.Tile)
        {
            humanoid.PlaceAt(tile);
        }
    }
}
