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
        if (humanoid.IsLeavingForPoacher)
        {
            if (!IsBusyWithPatient(simulation, humanoid))
            {
                JoinPoacher(simulation, humanoid);
            }

            return;
        }

        Tiredness.CheckIfNeedRest(simulation, humanoid);
        if (CheckIfWaitedTooLongForRaise(simulation, humanoid))
        {
            return;
        }

        Tiredness.TireIfWorking(simulation, humanoid);

        // Very unhappy staff ask for a raise after a while, unless world.debug_disable_salary_raise (on to begin with in
        // free build mode, switched from the debug menu).
        if (humanoid.Happiness < UnhappyForRaise && !Economy.Cheats.SalaryRaiseDisabled(simulation, simulation.Hospitals[humanoid.Employer]))
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
        Training.LearnOnTheJob(simulation, humanoid);
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
            humanoid.PoachedBy = -1;
        }
        else if (humanoid.PoachedBy >= 0)
        {
            // The poached member of staff's demand not met: they resign and join the poacher's hospital.
            humanoid.IsLeavingForPoacher = true;
        }
        else
        {
            Fire(simulation, humanoid);
        }
    }

    /// <summary>
    /// How unhappy a member of staff must be to listen to another hospital (the network game's poaching): below half.
    /// </summary>
    internal const decimal UnhappyForPoaching = 0.5m;

    /// <summary>
    /// The network game's poaching (the original's NETPLAY.TXT, "Poaching Staff"): another player asks a member of staff
    /// whether they would rather work for them. An unhappy one makes an outrageous pay demand of their employer, double
    /// their wage, which waits for an answer as a raise request does; a happy one says no.
    /// </summary>
    /// <returns>Whether the member of staff listened.</returns>
    internal static bool Poach(Simulation simulation, Humanoid humanoid, int player)
    {
        if (humanoid.Profile is not { } profile || humanoid.IsFired || humanoid.IsDespawning || humanoid.Employer < 0
            || humanoid.Employer == player || player < 0 || player >= simulation.Hospitals.Count
            || humanoid.RaiseRequest > 0 || humanoid.PoachedBy >= 0 || humanoid.IsLeavingForPoacher)
        {
            return false;
        }

        if (humanoid.Happiness >= UnhappyForPoaching)
        {
            return false;
        }

        humanoid.PoachedBy = player;
        humanoid.RaiseRequest = Math.Max(profile.Wage, RiseAmount(simulation, profile));
        humanoid.QuittingIn = QuittingDelay + simulation.Random.NextInclusive(0, QuittingSpread);
        return true;
    }

    /// <summary>
    /// The poached member of staff resigns and joins the poacher automatically: off their job, at their wage, employed
    /// by the poacher and put in the poacher's hospital, on the first free tile of the poacher's land outside rooms.
    /// </summary>
    internal static void JoinPoacher(Simulation simulation, Humanoid humanoid)
    {
        var poacher = humanoid.PoachedBy;
        if (poacher < 0 || poacher >= simulation.Hospitals.Count || humanoid.Profile is not { } profile)
        {
            humanoid.IsLeavingForPoacher = false;
            humanoid.PoachedBy = -1;
            return;
        }

        Detach(simulation, humanoid);
        humanoid.IsLeavingForPoacher = false;
        humanoid.PoachedBy = -1;
        humanoid.RaiseRequest = 0;
        humanoid.RaiseTimer = -1;
        humanoid.QuittingIn = -1;
        humanoid.Employer = poacher;
        humanoid.HospitalPlayer = poacher;
        humanoid.Behaviour = StaffHiring.NewBehaviour(profile.Class);
        if (PoacherTile(simulation, poacher) is { } tile)
        {
            humanoid.PlaceAt(tile);
        }
    }

    /// <summary>The first passable tile of the player's land outside rooms, row by row, or null.</summary>
    private static Maps.TilePosition? PoacherTile(Simulation simulation, int player)
    {
        var map = simulation.Map;
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var tile = map[x, y];
                if (tile.Parcel != 0 && map.GetParcelOwner(tile.Parcel) == player + 1 && tile.RoomId == 0 && tile.Has(Maps.TileFlags.Passable))
                {
                    return new Maps.TilePosition(x, y);
                }
            }
        }

        return null;
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

    internal static bool IsBusyWithPatient(Simulation simulation, Humanoid humanoid) => humanoid.Behaviour switch
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
            IncreaseWage(simulation, humanoid, humanoid.PoachedBy >= 0 ? humanoid.RaiseRequest : RiseAmount(simulation, humanoid.Profile!));
            humanoid.PoachedBy = -1;
            return false;
        }

        if (humanoid.PoachedBy >= 0)
        {
            humanoid.IsLeavingForPoacher = true;
            return true;
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

    /// <summary>
    /// <c>UIStaffManagement:payBonus</c>: if the balance is more than a tenth of the wage, that tenth is paid and the
    /// member of staff is half as happy again.
    /// </summary>
    /// <returns>Whether the bonus was paid.</returns>
    internal static bool PayBonus(Simulation simulation, Humanoid humanoid)
    {
        var profile = humanoid.Profile!;
        var hospital = simulation.Hospitals[humanoid.Employer];
        var bonus = profile.Wage / 10;
        if (hospital.Balance <= bonus)
        {
            return false;
        }

        Feelings.ChangeHappiness(simulation, humanoid, 0.5m);
        hospital.Spend(simulation.Hours, bonus, TransactionKind.PersonalBonus, profile.Class.ToString());
        return true;
    }

    /// <summary>
    /// <c>UIStaffManagement:increaseSalary</c>: a tenth more wage (<c>Staff:increaseWage</c>), which also cancels a
    /// pending raise request; at the highest salary the wage stays.
    /// </summary>
    internal static void IncreaseSalary(Simulation simulation, Humanoid humanoid)
    {
        IncreaseWage(simulation, humanoid, humanoid.Profile!.Wage / 10);
        humanoid.QuittingIn = -1;
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
        Detach(simulation, humanoid);
        var points = simulation.Arrivals.SpawnPointsOf(simulation.Map);
        var home = points.Count > 0 ? points[simulation.Random.Next(points.Count)] : humanoid.Tile;
        humanoid.Behaviour = new LeavingBehaviour(home);
    }

    /// <summary>
    /// Takes a member of staff off their job: out of the room they work in or are going to, off the reception desk they
    /// staff or are going to; they stop walking and any action, and are seen again.
    /// </summary>
    internal static void Detach(Simulation simulation, Humanoid humanoid)
    {
        switch (humanoid.Behaviour)
        {
            case DoctorBehaviour doctor:
                if (simulation.FindEntity(doctor.RoomId) is Room examining
                    && simulation.FindEntity(examining.PatientId) is Humanoid { Behaviour: PatientBehaviour { TrolleyUse: { } } patient } examined)
                {
                    // Picked up while using the room's object with the patient: the use stops, the patient waits again.
                    patient.AbortTrolleyUse(simulation, examined, humanoid);
                }

                LeaveRoom(simulation, humanoid, doctor.RoomId, doctor.Use?.OldTile);
                break;
            case NurseBehaviour nurse:
                nurse.AbortTreatment(simulation, humanoid);
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
