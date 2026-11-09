// The menu cheats that the simulation has the parts for, ported from CorsixTH CorsixTH/Lua/cheats.lua
// (Cheats:performCheat, announceCheat, cheatMoney, cheatPatient, cheatLose, cheatWin, cheatIncreasePrices,
// cheatDecreasePrices, cheatResetDeathCount, cheatMaxReputation, cheatEarthquake, cheatToggleEarthquake, cheatVip, cheatEmergency) and dialogs/resizables/cheats_dialog.lua
// (UICheats:buttonClicked).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

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
}

/// <summary>
/// CorsixTH's menu cheats. A cheat that works marks the hospital as having cheated
/// (<c>Cheats:announceCheat</c>), except losing the level.
/// </summary>
/// <remarks>
/// The other cheats wait for what they act on: research, epidemics, machines, and
/// jumping to the end of the month or year.
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
                hospital.Receive(simulation.Tick, 10_000, TransactionKind.Cheat, string.Empty);
                break;
            case CheatKind.CreatePatient:
                simulation.Arrivals.SpawnPatient(simulation, player);
                break;
            case CheatKind.LoseLevel:
                hospital.End(EndOutcome.Lost, null);
                return;
            case CheatKind.WinLevel:
                hospital.End(EndOutcome.Won, null);
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
            default:
                return;
        }

        hospital.Cheated = true;
    }
}
