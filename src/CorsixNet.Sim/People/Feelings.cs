// How warm and happy people feel day by day, ported from CorsixTH CorsixTH/Lua/entities/humanoid.lua
// (Humanoid:tickDay, changeAttribute, findObjectsInSquare), entities/humanoids/staff.lua (Staff:tickDay) and room.lua
// (Room:calculateHappinessFactor, countWindows).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Sim.People;

/// <summary>
/// The daily change in a person's warmth and happiness (CorsixTH's <c>tickDay</c>). Each day the person's warmth moves
/// a quarter towards the temperature of their tile; too cold (below 11 °C) or too hot (above 18 °C) makes them less
/// happy, comfortable a little happier. Staff also weigh their pay against their fair wage and like some objects near
/// them.
/// </summary>
/// <remarks>
/// Not yet: resting on the staff room's sofa or games, the cold and
/// hot mood icons, and patients' vomiting, thirst, toilets and yawning.
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

    /// <summary>Objects that cheer staff up when near them (<c>Staff:tickDay</c>'s <c>good_objects</c>).</summary>
    private static readonly (string ObjectType, decimal Happiness)[] GoodObjects =
    [
        ("extinguisher", 0.002m), // Makes you feel safe.
        ("bin", 0.001m),
        ("bookcase", 0.003m),
        ("skeleton", 0.002m),
        ("tv", 0.0005m),
    ];

    /// <summary>
    /// <c>e^(k + 0.5)</c> for k = 0, 1, …: <c>math.round(math.log(x))</c> (CorsixTH rounds half up) is how many of these
    /// <c>x</c> reaches, which keeps the logarithm out of the simulation.
    /// </summary>
    private static readonly decimal[] RoundedLogSteps =
    [
        1.6487212707001281m, 4.4816890703380645m, 12.182493960703473m, 33.115451958692312m, 90.017131300521814m,
        244.69193226422039m, 665.14163304436181m, 1808.0424144560632m, 4914.7688402991344m, 13359.726829661872m,
    ];

    /// <summary>The external wall's window sprites, which CorsixTH's <c>countWindows</c> looks for (<c>window_tile</c>).</summary>
    private static readonly ushort[] WindowTiles = [116, 117, 118, 119, 120, 121, 124, 125, 126, 127];

    /// <summary>One person's day, from <see cref="Simulation"/>'s end of the day.</summary>
    internal static void OnEndDay(Simulation simulation, Humanoid humanoid)
    {
        // No use for those going home.
        if (humanoid.IsDespawning || humanoid.IsFired || humanoid.Behaviour is PatientBehaviour { Phase: PatientPhase.GoingHome })
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
        else if (humanoid.Behaviour is PatientBehaviour patient)
        {
            OnEndDayPatient(simulation, humanoid, patient);
        }
    }

    /// <summary>
    /// The rest of <c>Patient:tickDay</c> after <c>Humanoid:tickDay</c>: the health checks, what the patient sees
    /// outside rooms, and the day's loss of health.
    /// </summary>
    private static void OnEndDayPatient(Simulation simulation, Humanoid humanoid, PatientBehaviour patient)
    {
        if (!patient.OnEndDayHealth(simulation, humanoid))
        {
            return;
        }

        // In the corridor (not in a room, nor going in or out), plants, extinguishers and standing about count.
        if (patient.Phase is < PatientPhase.EnteringRoom or > PatientPhase.LeavingRoom
            && simulation.Map[humanoid.Tile.X, humanoid.Tile.Y].RoomId == 0)
        {
            var plants = ObjectsInSight(simulation, humanoid, "plant");
            if (plants.Count > 0)
            {
                var plant = RandomEntry(simulation, plants);
                ChangeHappiness(simulation, humanoid, -0.0003m + ((plant.Plant?.PleasingFactor ?? PlantState.Phases) * 0.0001m));
            }

            // It always makes you happy to see you are in a safe place.
            var extinguishers = ObjectsInSight(simulation, humanoid, "extinguisher");
            if (extinguishers.Count > 0)
            {
                RandomEntry(simulation, extinguishers);
                ChangeHappiness(simulation, humanoid, 0.0002m);
            }

            // Sitting makes happy, standing and walking do not; there are no benches yet.
            ChangeHappiness(simulation, humanoid, -0.00002m);

            // Seeing litter makes unhappy, pee and puke more so. Vomiting at it is not ported yet.
            SeeLitter(simulation, humanoid);
        }

        patient.Health = Math.Max(patient.Health - PatientBehaviour.DailyHealthLoss, 0m);
    }

    /// <summary><c>getRandomEntryFromArray</c>, which draws only when there is more than one.</summary>
    private static RoomObject RandomEntry(Simulation simulation, List<RoomObject> objects) =>
        objects.Count == 1 ? objects[0] : objects[simulation.Random.NextInclusive(1, objects.Count) - 1];

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

        // Working while tired makes unhappy, resting happier.
        Tiredness.OnEndDay(simulation, humanoid);

        // It is nice to see plants, but dead plants make unhappy; a random one of those nearby counts.
        var plants = ObjectsInSight(simulation, humanoid, "plant");
        if (plants.Count > 0)
        {
            // getRandomEntryFromArray draws only when there is more than one.
            var plant = RandomEntry(simulation, plants);
            var pleasing = plant.Plant?.PleasingFactor ?? PlantState.Phases;
            ChangeHappiness(simulation, humanoid, -0.003m + (pleasing * 0.001m));
        }

        foreach (var (objectType, happiness) in GoodObjects)
        {
            ChangeHappiness(simulation, humanoid, ObjectsInSight(simulation, humanoid, objectType).Count * happiness);
        }

        // A room with windows and space to spare is nicer to be in.
        var roomId = simulation.Map[humanoid.Tile.X, humanoid.Tile.Y].RoomId;
        if (roomId != 0 && simulation.FindEntity(roomId) is Room { IsActive: true } room)
        {
            ChangeHappiness(simulation, humanoid, RoomHappinessFactor(simulation, room));
        }

        // Seeing litter makes unhappy, pee and puke more so (the end of Staff:tickDay).
        SeeLitter(simulation, humanoid);
    }

    /// <summary>
    /// The litter part of <c>Patient:tickDay</c> and <c>Staff:tickDay</c>: each piece in sight costs 0.0002 happiness if it is
    /// rubbish (<c>anyLitter</c>), otherwise 0.0004.
    /// </summary>
    private static void SeeLitter(Simulation simulation, Humanoid humanoid)
    {
        var map = simulation.Map;
        var roomId = map[humanoid.Tile.X, humanoid.Tile.Y].RoomId;
        foreach (var entity in simulation.Entities)
        {
            if (entity is Litter { IsRemoved: false } litter
                && Math.Abs(litter.Tile.X - humanoid.Tile.X) <= SightRange
                && Math.Abs(litter.Tile.Y - humanoid.Tile.Y) <= SightRange
                && map.Contains(litter.Tile.X, litter.Tile.Y)
                && map[litter.Tile.X, litter.Tile.Y].RoomId == roomId)
            {
                ChangeHappiness(simulation, humanoid, litter.IsRubbish ? -0.0002m : -0.0004m);
            }
        }
    }

    /// <summary>
    /// <c>Room:calculateHappinessFactor</c>: a thousandth for each step of the rounded natural logarithm of the window
    /// count (doubled in a staff room), and likewise of how many times the room's area is its smallest allowed area.
    /// </summary>
    /// <remarks>
    /// CorsixTH counts the tiles from the room's corner to one past its far corner whose north or west wall is one of
    /// the external wall's windows. The windows a player builds into a room's walls use other sprites, so only the
    /// building's own windows along a room count.
    /// </remarks>
    internal static decimal RoomHappinessFactor(Simulation simulation, Room room)
    {
        var map = simulation.Map;
        var windows = 0;
        for (var x = room.Area.X; x <= room.Area.Right; x++)
        {
            for (var y = room.Area.Y; y <= room.Area.Bottom; y++)
            {
                if (map.Contains(x, y) && (Array.IndexOf(WindowTiles, map[x, y].NorthWall) >= 0 || Array.IndexOf(WindowTiles, map[x, y].WestWall) >= 0))
                {
                    windows++;
                }
            }
        }

        var factor = 0m;
        if (windows > 0)
        {
            if (room.RoomType == "staff_room")
            {
                // Staff are pleased to rest in a staff room with windows.
                windows *= 2;
            }

            factor += RoundedLog(windows) / 1000m;
        }

        if (simulation.Content.FindRoom(room.RoomType) is { MinimumSize: > 0 } definition)
        {
            var extraSpace = room.Area.Width * room.Area.Height / (decimal)(definition.MinimumSize * definition.MinimumSize);
            if (extraSpace > 1)
            {
                factor += RoundedLog(extraSpace) / 1000m;
            }
        }

        return factor;
    }

    /// <summary><c>math.round(math.log(x))</c> for <paramref name="x"/> ≥ 1.</summary>
    private static int RoundedLog(decimal x)
    {
        var steps = 0;
        while (steps < RoundedLogSteps.Length && x >= RoundedLogSteps[steps])
        {
            steps++;
        }

        return steps;
    }

    /// <summary>
    /// <c>Humanoid:findObjectsInSquare</c>: the objects of a type within <see cref="SightRange"/> tiles in the same room
    /// as the person (or both in the corridor), found by the tile each object stands on, column by column as CorsixTH
    /// searches.
    /// </summary>
    private static List<RoomObject> ObjectsInSight(Simulation simulation, Humanoid humanoid, string objectType)
    {
        var map = simulation.Map;
        var roomId = map[humanoid.Tile.X, humanoid.Tile.Y].RoomId;
        var found = new List<RoomObject>();
        foreach (var entity in simulation.Entities)
        {
            if (entity is RoomObject placed
                && placed.ObjectType == objectType
                && Math.Abs(placed.Tile.X - humanoid.Tile.X) <= SightRange
                && Math.Abs(placed.Tile.Y - humanoid.Tile.Y) <= SightRange
                && map.Contains(placed.Tile.X, placed.Tile.Y)
                && map[placed.Tile.X, placed.Tile.Y].RoomId == roomId)
            {
                found.Add(placed);
            }
        }

        // Stable, so objects on one tile keep the order they were placed in.
        return [.. found.OrderBy(placed => placed.Tile.X).ThenBy(placed => placed.Tile.Y)];
    }
}
