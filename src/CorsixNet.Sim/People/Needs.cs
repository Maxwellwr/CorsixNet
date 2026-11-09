// Thirst and the need for the toilet, ported from CorsixTH CorsixTH/Lua/entities/humanoids/patient.lua
// (Patient:tickDay, _dailyBowelChecks, _handleExcessThirst, Patient:setDisease).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Pathfinding;

namespace CorsixNet.Sim.People;

/// <summary>
/// A patient's thirst and need for the toilet. Each day in the hospital, except for emergency patients, thirst grows
/// by 0.004 to 0.008 plus a fiftieth of their warmth, and the need for the toilet by 0.002 to 0.008 (0.008 to 0.026 for
/// gut rot and the squits). Above 0.7 thirst costs happiness every day and the patient, in a corridor, looks for a drinks
/// machine within 8 steps every few days and goes to buy a can (<see cref="PatientBehaviour.GoForDrink"/>). Above 0.75
/// need for the toilet a patient does it on the floor four days in ten, otherwise goes to the toilets.
/// </summary>
internal static class Needs
{
    /// <summary>A patient starts looking for a drink above this thirst.</summary>
    internal const decimal ExcessThirst = 0.7m;

    /// <summary>Above this need for the toilet a patient goes, or does it on the floor (<c>_dailyBowelChecks</c>).</summary>
    internal const decimal ToiletNeedLimit = 0.75m;

    /// <summary>How far a thirsty patient looks for a drinks machine (<c>findObjectNear(self, "drinks_machine", 8)</c>).</summary>
    private const int MachineDistance = 8;

    private const string DrinksMachineType = "drinks_machine";

    /// <summary>Steps of CorsixTH's <c>math.random()</c> here.</summary>
    private const int RandomSteps = 1_000_000;

    /// <summary>CorsixTH's <c>math.random()</c>: from 0 up to, not including, 1.</summary>
    internal static decimal Random(SimRandom random) => random.Next(RandomSteps) / (decimal)RandomSteps;

    /// <summary>The thirst and toilet part of <c>Patient:tickDay</c>, after the day's loss of health.</summary>
    internal static void OnEndDay(Simulation simulation, Humanoid humanoid, PatientBehaviour patient)
    {
        if (patient.EmergencyIndex != 0)
        {
            return;
        }

        patient.Thirst = Clamp(patient.Thirst + (humanoid.Warmth * 0.02m) + (0.004m * Random(simulation.Random)) + 0.004m);

        // Patient:_dailyBowelChecks.
        var moreLooUse = patient.Disease?.MoreLooUse ?? false;
        patient.ToiletNeed = Clamp(patient.ToiletNeed + (moreLooUse
            ? (0.018m * Random(simulation.Random)) + 0.008m
            : (0.006m * Random(simulation.Random)) + 0.002m));
        if (patient.ToiletNeed > ToiletNeedLimit && simulation.Content.FindPatientAnimations(humanoid.Type)?.Pee is not null
            && simulation.Map[humanoid.Tile.X, humanoid.Tile.Y].RoomId == 0
            && patient.Phase is not (>= PatientPhase.EnteringRoom and <= PatientPhase.LeavingRoom))
        {
            HandleToiletNeed(simulation, humanoid, patient);
        }

        if (patient.Thirst > ExcessThirst)
        {
            HandleExcessThirst(simulation, humanoid, patient);
        }
    }

    /// <summary>
    /// <c>Patient:handleToiletNeed</c>: four times in ten the patient does it on the floor; otherwise, unless waiting for a
    /// decision, they want the toilet and go to the toilets, or remember the hospital has none.
    /// </summary>
    private static void HandleToiletNeed(Simulation simulation, Humanoid humanoid, PatientBehaviour patient)
    {
        if (simulation.Random.NextInclusive(1, 10) < 5)
        {
            patient.Pee(simulation, humanoid);
            patient.ToiletNeed = Clamp(patient.ToiletNeed - (0.5m + (Random(simulation.Random) * 0.15m)));
            patient.GoingToToilet = ToiletVisit.No;
        }
        else if (patient.GoingToToilet == ToiletVisit.No && patient.Phase != PatientPhase.Waiting)
        {
            patient.IsNeedingToilet = true;
            if (!RoomVisits.HasRoom(simulation, humanoid.HospitalPlayer, RoomVisits.ToiletsRoom))
            {
                // Reset when toilets are built (Patient:notifyNewRoom).
                patient.GoingToToilet = ToiletVisit.NoToilets;
            }
            else
            {
                patient.GoToToilets(simulation, humanoid);
            }
        }
    }

    /// <summary>
    /// <c>Patient:_handleExcessThirst</c>: unhappy and thirsty; in a corridor, unless they looked a day or few ago, the
    /// patient looks for a drinks machine nearby and, seven times in ten, would go and buy a drink.
    /// </summary>
    private static void HandleExcessThirst(Simulation simulation, Humanoid humanoid, PatientBehaviour patient)
    {
        Feelings.ChangeHappiness(simulation, humanoid, -0.002m);
        patient.IsThirsty = true;

        // Already on the way to a drinks machine.
        if (patient.DrinksMachineId != 0)
        {
            return;
        }

        // Don't look for a drinks machine too often.
        if (patient.DrinkTimeout > 0)
        {
            patient.DrinkTimeout--;
            return;
        }

        // Only in the corridors: not in a room, nor going in or out, nor on the way home.
        if (patient.Phase is >= PatientPhase.EnteringRoom and <= PatientPhase.LeavingRoom
            || patient.Phase == PatientPhase.GoingHome
            || simulation.Map[humanoid.Tile.X, humanoid.Tile.Y].RoomId != 0)
        {
            return;
        }

        if (FindMachineNear(simulation, humanoid) is not { } machine || simulation.Random.NextInclusive(1, 10) < 3)
        {
            patient.DrinkTimeout = simulation.Random.NextInclusive(2, 4);
            return;
        }

        patient.GoForDrink(simulation, humanoid, machine);
    }

    /// <summary><c>World:findObjectNear(self, "drinks_machine", 8)</c>, by CorsixTH's object visitor: the nearest machine.</summary>
    private static RoomObject? FindMachineNear(Simulation simulation, Humanoid humanoid)
    {
        var machines = new Dictionary<TilePosition, RoomObject>();
        foreach (var entity in simulation.Entities)
        {
            if (entity is RoomObject { ObjectType: DrinksMachineType } machine)
            {
                machines.TryAdd(machine.Tile, machine);
            }
        }

        RoomObject? found = null;
        if (machines.Count > 0)
        {
            ObjectVisitor.Visit(simulation.Map, humanoid.Tile, MachineDistance, tile => machines.TryGetValue(tile, out found));
        }

        return found;
    }

    private static decimal Clamp(decimal value) => Math.Clamp(value, 0m, 1m);
}
