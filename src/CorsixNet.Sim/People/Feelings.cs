// How warm and happy people feel day by day, ported from CorsixTH CorsixTH/Lua/entities/humanoid.lua
// (Humanoid:tickDay, changeAttribute, findObjectsInSquare) and entities/humanoids/staff.lua (Staff:tickDay).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Sim.People;

/// <summary>
/// The daily change in a person's warmth and happiness (CorsixTH's <c>tickDay</c>). Each day the person's warmth moves
/// a quarter towards the temperature of their tile; too cold (below 11 °C) or too hot (above 18 °C) makes them less
/// happy, comfortable a little happier. Staff also weigh their pay against their fair wage and like some objects near
/// them.
/// </summary>
/// <remarks>
/// Not yet: tiredness (staff working while tired or resting), resting on the staff room's sofa or games, a room's
/// windows and space (<c>Room:calculateHappinessFactor</c>), the cold and hot mood icons, and patients' own daily
/// effects (waiting, health, vomit, thirst). Plants count as healthy, since they do not wilt yet.
/// </remarks>
internal static class Feelings
{
    /// <summary>11 °C: below this a person is cold (<c>min_comfort_temp</c>).</summary>
    private const decimal MinComfort = 0.22m;

    /// <summary>18 °C: above this a person is hot (<c>max_comfort_temp</c>).</summary>
    private const decimal MaxComfort = 0.36m;

    /// <summary>How much being cold or hot costs per unit of discomfort (<c>decrease_factor</c>).</summary>
    private const decimal DecreaseFactor = 0.10m;

    /// <summary>What a comfortable day brings (<c>increase_happiness</c>).</summary>
    private const decimal ComfortHappiness = 0.005m;

    /// <summary>How far around a member of staff they see objects (<c>findObjectsInSquare(2, ...)</c>).</summary>
    private const int SightRange = 2;

    /// <summary>How healthy an unwilted plant is (<c>Plant:isPleasingFactor</c> at state 0).</summary>
    private const int HealthyPlant = 5;

    /// <summary>Objects that cheer staff up when near them (<c>Staff:tickDay</c>'s <c>good_objects</c>).</summary>
    private static readonly (string ObjectType, decimal Happiness)[] GoodObjects =
    [
        ("extinguisher", 0.002m), // Makes you feel safe.
        ("bin", 0.001m),
        ("bookcase", 0.003m),
        ("skeleton", 0.002m),
        ("tv", 0.0005m),
    ];

    /// <summary>One person's day, from <see cref="Simulation"/>'s end of the day.</summary>
    internal static void OnEndDay(Simulation simulation, Humanoid humanoid)
    {
        // No use for those going home.
        if (humanoid.IsDespawning || humanoid.Behaviour is PatientBehaviour { Phase: PatientPhase.GoingHome })
        {
            return;
        }

        var temperature = simulation.Temperatures[humanoid.Tile.X, humanoid.Tile.Y] / (decimal)TileTemperatures.Max;
        humanoid.Warmth = (humanoid.Warmth * 0.75m) + (temperature * 0.25m);

        // Only people of a hospital mind the temperature: the demo's wanderers have none.
        var isStaff = humanoid.Profile is not null && humanoid.Employer >= 0;
        if (!isStaff && humanoid.HospitalPlayer < 0)
        {
            return;
        }

        var warmth = humanoid.Warmth;
        if (warmth < MinComfort)
        {
            ChangeHappiness(simulation, humanoid, -DecreaseFactor * (MinComfort - warmth));
        }
        else if (warmth > MaxComfort)
        {
            ChangeHappiness(simulation, humanoid, -DecreaseFactor * (warmth - MaxComfort));
        }
        else
        {
            ChangeHappiness(simulation, humanoid, ComfortHappiness);
        }

        if (isStaff && humanoid.Profile!.Class != StaffClass.Receptionist)
        {
            OnEndDayStaff(simulation, humanoid, humanoid.Profile);
        }
    }

    /// <summary>
    /// <c>Humanoid:changeAttribute</c> for happiness: kept between 0 and 1, and always 1 for a receptionist or a member
    /// of staff on the level's highest salary.
    /// </summary>
    internal static void ChangeHappiness(Simulation simulation, Humanoid humanoid, decimal amount)
    {
        if (humanoid.Profile is { } profile && (profile.Class == StaffClass.Receptionist || profile.Wage >= simulation.Rules.MaxSalary))
        {
            humanoid.Happiness = 1m;
            return;
        }

        humanoid.Happiness = Math.Clamp(humanoid.Happiness + amount, 0m, 1m);
    }

    /// <summary>The staff's part of <c>Staff:tickDay</c>.</summary>
    private static void OnEndDayStaff(Simulation simulation, Humanoid humanoid, StaffProfile profile)
    {
        // Paid too little makes unhappy, too much happy.
        var fairWage = profile.FairWage(simulation.Rules.Staff);
        ChangeHappiness(simulation, humanoid, 0.05m * (profile.Wage - fairWage) / (fairWage != 0 ? fairWage : 1));

        // It is nice to see plants; a random one of those nearby counts.
        var plants = ObjectsInSight(simulation, humanoid, "plant");
        if (plants > 0)
        {
            // getRandomEntryFromArray draws only when there is more than one.
            if (plants > 1)
            {
                simulation.Random.NextInclusive(1, plants);
            }

            ChangeHappiness(simulation, humanoid, -0.003m + (HealthyPlant * 0.001m));
        }

        foreach (var (objectType, happiness) in GoodObjects)
        {
            ChangeHappiness(simulation, humanoid, ObjectsInSight(simulation, humanoid, objectType) * happiness);
        }
    }

    /// <summary>
    /// <c>Humanoid:findObjectsInSquare</c>: how many objects of a type stand within <see cref="SightRange"/> tiles in
    /// the same room as the person (or both in the corridor), counted by the tile each object stands on.
    /// </summary>
    private static int ObjectsInSight(Simulation simulation, Humanoid humanoid, string objectType)
    {
        var map = simulation.Map;
        var roomId = map[humanoid.Tile.X, humanoid.Tile.Y].RoomId;
        var count = 0;
        foreach (var entity in simulation.Entities)
        {
            if (entity is RoomObject placed
                && placed.ObjectType == objectType
                && Math.Abs(placed.Tile.X - humanoid.Tile.X) <= SightRange
                && Math.Abs(placed.Tile.Y - humanoid.Tile.Y) <= SightRange
                && map.Contains(placed.Tile.X, placed.Tile.Y)
                && map[placed.Tile.X, placed.Tile.Y].RoomId == roomId)
            {
                count++;
            }
        }

        return count;
    }
}
