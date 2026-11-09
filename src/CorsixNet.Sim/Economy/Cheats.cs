// The menu cheats that the simulation has the parts for, ported from CorsixTH CorsixTH/Lua/cheats.lua
// (Cheats:performCheat, announceCheat, cheatMoney, cheatPatient, cheatLose, cheatWin, cheatIncreasePrices,
// cheatDecreasePrices, cheatResetDeathCount, cheatMaxReputation, cheatEarthquake, cheatToggleEarthquake, cheatVip,
// cheatEmergency, cheatEpidemic, cheatToggleEpidemic, cheatMonth, cheatYear) and
// dialogs/resizables/cheats_dialog.lua (UICheats:buttonClicked).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Calendar;

namespace CorsixNet.Sim.Economy;

/// <summary>A cheat from CorsixTH's cheats window, by its name in <c>cheat_list</c>.</summary>
public enum CheatKind : byte
{
    /// <summary><c>money</c>: 10,000 from nowhere.</summary>
    Money = 1,

    /// <summary><c>create_patient</c>: a patient for the hospital now, if it has a staffed desk.</summary>
    CreatePatient = 2,

    /// <summary><c>lose_level</c>: the level is lost; this one does not count as cheating.</summary>
    LoseLevel = 3,

    /// <summary><c>win_level</c>: the level is won.</summary>
    WinLevel = 4,

    /// <summary><c>increase_prices</c>: every price in the casebook 50 points up, to at most 200 %.</summary>
    IncreasePrices = 5,

    /// <summary><c>decrease_prices</c>: every price in the casebook 50 points down, to at least 50 %.</summary>
    DecreasePrices = 6,

    /// <summary><c>reset_death_count</c>: the deaths so far and this year are forgotten.</summary>
    ResetDeathCount = 7,

    /// <summary><c>max_reputation</c>: the reputation goes to 1,000.</summary>
    MaxReputation = 8,

    /// <summary><c>earthquake</c>: the planned earthquake comes today, or a small one; not while one is under way or they are off.</summary>
    Earthquake = 9,

    /// <summary><c>toggle_earthquake</c>: earthquakes off, or back on. These are the world's, so every hospital's.</summary>
    ToggleEarthquakes = 10,

    /// <summary><c>vip</c>: a VIP asks to visit now (<c>Hospital:createVip</c>), even if one is already on the way.</summary>
    Vip = 11,

    /// <summary><c>emergency</c>: a random emergency is offered now; not without a heliport, a staffed desk or a known disease.</summary>
    Emergency = 12,

    /// <summary><c>epidemic</c>: a contagious patient comes, starting or joining an epidemic; not without a staffed desk or a contagious disease.</summary>
    Epidemic = 13,

    /// <summary><c>toggle_epidemic</c>: epidemics off, cancelling those under way, or back on.</summary>
    ToggleEpidemics = 14,

    /// <summary><c>end_month</c>: the calendar jumps to the last hour of the month, which then ends.</summary>
    EndMonth = 15,

    /// <summary><c>end_year</c>: the calendar jumps to the last hour of the year, which then ends.</summary>
    EndYear = 16,

    /// <summary>Roujin's challenge (<c>spawn_rate_cheat</c>), on or off: 40 more patients a month.</summary>
    ToggleSpawnRate = 17,

    /// <summary><c>no_rest_cheat</c>, on or off: the hospital's staff do not tire, and are rested when it goes on.</summary>
    ToggleNoRest = 18,

    /// <summary><c>queuejump</c>, on or off: nearly dead patients go ahead in queues.</summary>
    ToggleQueueJump = 19,

    /// <summary><c>super_doctor</c>, on or off: the doctors for hire are all consultants with every ability.</summary>
    ToggleSuperDoctor = 20,

    /// <summary><c>all_research</c>: every diagnosis and cure object to research is discovered, with the rooms they make buildable.</summary>
    AllResearch = 21,
}

/// <summary>CorsixTH's fax code cheats (<c>toggle_cheats</c>), which stay on until the code is sent again.</summary>
[Flags]
public enum ToggleCheats : byte
{
    None = 0,

    /// <summary>Roujin's challenge (<c>spawn_rate_cheat</c>).</summary>
    SpawnRate = 1,

    /// <summary><c>no_rest_cheat</c>.</summary>
    NoRest = 2,

    /// <summary><c>queuejump</c>.</summary>
    QueueJump = 4,

    /// <summary><c>super_doctor</c>.</summary>
    SuperDoctor = 8,

    All = SpawnRate | NoRest | QueueJump | SuperDoctor,
}

/// <summary>
/// CorsixTH's menu cheats. A cheat that works marks the hospital as having cheated
/// (<c>Cheats:announceCheat</c>), except losing the level.
/// </summary>
/// <remarks>
/// The other cheats wait for what they act on: showing the infected before an epidemic shows, and machines.
/// </remarks>
public static class Cheats
{
    /// <summary>The cheat's price change in percent (CorsixTH's 0.5).</summary>
    private const int PriceStep = 50;

    internal static void Perform(Simulation simulation, int player, CheatKind kind)
    {
        if (player < 0 || player >= simulation.Hospitals.Count)
        {
            return;
        }

        var hospital = simulation.Hospitals[player];
        switch (kind)
        {
            case CheatKind.Money:
                hospital.Receive(simulation.Hours, 10_000, TransactionKind.Cheat, string.Empty);
                break;
            case CheatKind.CreatePatient:
                simulation.Arrivals.SpawnPatient(simulation, player);
                break;
            case CheatKind.LoseLevel:
                hospital.End(EndOutcome.Lost, null, simulation.Random);
                return;
            case CheatKind.WinLevel:
                hospital.End(EndOutcome.Won, null, simulation.Random);
                break;
            case CheatKind.IncreasePrices:
            case CheatKind.DecreasePrices:
                var step = kind == CheatKind.IncreasePrices ? PriceStep : -PriceStep;
                foreach (var entry in hospital.Casebook.Values)
                {
                    entry.PricePercent = Math.Clamp(entry.PricePercent + step, DiseaseCase.MinPrice, DiseaseCase.MaxPrice);
                }

                break;
            case CheatKind.ResetDeathCount:
                hospital.Deaths = 0;
                hospital.DeathsThisYear = 0;
                break;
            case CheatKind.MaxReputation:
                hospital.UnconditionalChangeReputation(Hospital.MaxReputation);
                break;
            case CheatKind.Earthquake:
                if (!simulation.Earthquakes.Create(simulation.Date, simulation.Random))
                {
                    return;
                }

                break;
            case CheatKind.ToggleEarthquakes:
                simulation.Earthquakes.ToggleDisabled();
                break;
            case CheatKind.Vip:
                VipVisits.Invite(simulation, hospital);
                break;
            case CheatKind.Emergency:
                if (!Disasters.Emergencies.CreateRandom(simulation, hospital))
                {
                    return;
                }

                break;
            case CheatKind.Epidemic:
                if (!Disasters.Epidemics.SpawnContagiousPatient(simulation, hospital))
                {
                    return;
                }

                break;
            case CheatKind.ToggleEpidemics:
                Disasters.Epidemics.ToggleDisabled(simulation, hospital);
                break;
            case CheatKind.EndMonth:
                var date = simulation.Date;
                simulation.SkipToLastHour(date.Month == 12 ? new GameDate(date.Year + 1, 1, 1, 0) : new GameDate(date.Year, date.Month + 1, 1, 0));
                break;
            case CheatKind.EndYear:
                simulation.SkipToLastHour(new GameDate(simulation.Date.Year + 1, 1, 1, 0));
                break;
            case CheatKind.ToggleSpawnRate:
                Toggle(simulation, hospital, ToggleCheats.SpawnRate);
                break;
            case CheatKind.ToggleNoRest:
                Toggle(simulation, hospital, ToggleCheats.NoRest);
                break;
            case CheatKind.ToggleQueueJump:
                Toggle(simulation, hospital, ToggleCheats.QueueJump);
                break;
            case CheatKind.ToggleSuperDoctor:
                Toggle(simulation, hospital, ToggleCheats.SuperDoctor);
                break;
            case CheatKind.AllResearch:
                hospital.Research.DiscoverAll(simulation, hospital);
                break;
            default:
                return;
        }

        hospital.Cheated = true;
    }

    /// <summary>
    /// <c>Cheats:toggleCheat</c>: the fax code cheat goes on or off, either way marking the hospital as having cheated
    /// (<c>announceCheat</c>).
    /// </summary>
    private static void Toggle(Simulation simulation, Hospital hospital, ToggleCheats cheat)
    {
        var on = (hospital.ActiveCheats & cheat) == 0;
        hospital.ActiveCheats = on ? hospital.ActiveCheats | cheat : hospital.ActiveCheats & ~cheat;
        switch (cheat)
        {
            case ToggleCheats.SpawnRate when !on:
                // Cheats:roujinOff: the month's remaining patients are called off to give the player a break.
                simulation.Arrivals.ClearSpawnDates();
                break;
            case ToggleCheats.NoRest when on:
                // Cheats:noRestOn: the hospital's staff are rested at once.
                foreach (var entity in simulation.Entities)
                {
                    if (entity is Entities.Humanoid { Profile: not null } staff && staff.Employer == hospital.Player)
                    {
                        staff.Fatigue = 0;
                    }
                }

                break;
            case ToggleCheats.SuperDoctor:
                // Cheats:superDoctorOn and Off: new candidates for hire at once.
                simulation.MakeAvailableStaff(simulation.Date.MonthOfGame);
                break;
        }
    }
}
