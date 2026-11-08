// Ported from CorsixTH CorsixTH/Lua/world.lua (calculateSpawnTiles, spawnPatient, _findClosestSpawnToDesk,
// onEndMonth, _prepareSpawnDates, _prepareSpawnHours) and
// CorsixTH/Lua/app.lua (math.p_random).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.People;

/// <summary>
/// When patients arrive, as CorsixTH decides it: each month the spawn rate (patients per month) changes
/// by the level's <c>popn</c> setting and the arrivals of the month are spread over its days with
/// Poisson-distributed gaps; each day the arrivals of the next day get random hours. A patient only comes
/// to a hospital with a staffed reception desk, and enters the map at one of its spawn points.
/// </summary>
/// <remarks>
/// CorsixTH waits for the hospital to open (the countdown at the start of a level) and sends every
/// patient to the first hospital; both still apply here, apart from the countdown.
/// </remarks>
public sealed class PatientArrivals
{
    /// <summary>Days of a month are numbered 1 to 31.</summary>
    private const int MaxDays = 32;

    private readonly int[] _spawnDates = new int[MaxDays];
    private readonly int[] _spawnHours = new int[GameDate.HoursPerDay];
    private List<TilePosition>? _spawnPoints;

    private PatientArrivals()
    {
    }

    /// <summary>Patients per month.</summary>
    public int SpawnRate { get; private set; }

    /// <summary>How much <see cref="SpawnRate"/> changes at the end of each month.</summary>
    public int MonthlyIncrease { get; private set; }

    /// <summary>Whether the first hospital has had a patient yet; the first comes by the shortest way to a desk.</summary>
    public bool HadPatients { get; private set; }

    /// <summary>
    /// Where patients enter and leave the map: up to 8 tiles on each edge that can be reached from the
    /// hospital. Derived from the map, which only changes inside the hospital; not saved.
    /// </summary>
    public IReadOnlyList<TilePosition> SpawnPoints => _spawnPoints ?? [];

    /// <summary>Patients arriving on each day of the month (index 1 to 31).</summary>
    public IReadOnlyList<int> SpawnDates => _spawnDates;

    /// <summary>Patients arriving at each hour of the coming day.</summary>
    public IReadOnlyList<int> SpawnHours => _spawnHours;

    /// <summary>Sets up arrivals for a new game, as <c>World:World</c>.</summary>
    internal static PatientArrivals Start(Simulation simulation)
    {
        var population = simulation.Rules.Population;
        var arrivals = new PatientArrivals();
        arrivals.SpawnRate = population.Count > 0 ? population[0].Change : 0;
        arrivals.MonthlyIncrease = arrivals.SpawnRate;
        arrivals.PrepareSpawnDates(simulation, firstMonth: true);
        return arrivals;
    }

    /// <summary>The spawn points of the map, worked out the first time they are needed.</summary>
    internal IReadOnlyList<TilePosition> SpawnPointsOf(LevelMap map) => _spawnPoints ??= CalculateSpawnTiles(map);

    /// <summary><c>World:onEndDay</c>: the next day's patients get their hours.</summary>
    internal void OnEndDay(Simulation simulation) => PrepareSpawnHours(simulation);

    /// <summary>
    /// Runs before the entities update on each tick, after the end of a day: the patients of the coming
    /// hour arrive (CorsixTH's <c>spawn_hours</c> in <c>World:onTick</c>).
    /// </summary>
    internal void SpawnPatients(Simulation simulation)
    {
        // As in CorsixTH, patients come to the first hospital, and only once it has opened.
        if (simulation.Hospitals.Count == 0 || !simulation.Hospitals[0].IsOpen)
        {
            return;
        }

        var count = _spawnHours[GameDate.FromHours(simulation.Tick + 1).Hour];
        for (var i = 0; i < count; i++)
        {
            SpawnPatient(simulation, player: 0);
        }
    }

    /// <summary>
    /// <c>World:spawnPatient</c>: a patient for the hospital, if it has a staffed reception desk, at a random
    /// spawn point, or for the hospital's first patient the one with the shortest way to a desk.
    /// </summary>
    internal Humanoid? SpawnPatient(Simulation simulation, int player)
    {
        SpawnPointsOf(simulation.Map);
        if (player >= simulation.Hospitals.Count || _spawnPoints!.Count == 0)
        {
            return null;
        }

        // The disease is chosen first, even when no patient comes after all.
        var hospital = simulation.Hospitals[player];
        var disease = PatientCare.NewPatientDisease(simulation, hospital);
        var desks = ReceptionDesks.OfHospital(simulation, player, staffedOnly: true).ToList();
        if (desks.Count == 0)
        {
            return null;
        }

        var spawn = !HadPatients && ClosestSpawnToDesk(simulation, desks) is { } closest
            ? closest
            : _spawnPoints[simulation.Random.Next(_spawnPoints.Count)];
        HadPatients = true;
        var look = disease?.Look ?? PatientLook.Standard;
        var patient = simulation.AddEntity(id => Humanoid.CreatePatient(id, spawn, simulation.Random, look, player));
        var behaviour = new PatientBehaviour();
        if (disease is not null)
        {
            behaviour.SetDisease(disease, PatientCare.ChooseInsuranceCompany(simulation.Random));
        }

        patient.Behaviour = behaviour;
        hospital.Visitors++;
        hospital.VisitorsThisYear++;
        return patient;
    }

    private TilePosition? ClosestSpawnToDesk(Simulation simulation, List<RoomObject> desks)
    {
        TilePosition? best = null;
        var bestDistance = 0;
        foreach (var spawn in SpawnPoints)
        {
            foreach (var desk in desks)
            {
                if (simulation.Pathfinder.FindDistance(simulation.Map, desk.Tile, spawn) is { } distance && (best is null || distance < bestDistance))
                {
                    best = spawn;
                    bestDistance = distance;
                }
            }
        }

        return best;
    }

    /// <summary><c>World:onEndMonth</c>: the spawn rate changes by the level's setting for the month that ends.</summary>
    internal void OnEndMonth(Simulation simulation)
    {
        var month = simulation.Date.MonthOfGame;
        foreach (var entry in simulation.Rules.Population)
        {
            if (entry.Month == month - 1)
            {
                MonthlyIncrease = entry.Change;
                break;
            }
        }

        SpawnRate += MonthlyIncrease;
        PrepareSpawnDates(simulation, firstMonth: false);
    }

    /// <summary><c>World:_prepareSpawnDates</c>: spreads the month's patients over its days.</summary>
    private void PrepareSpawnDates(Simulation simulation, bool firstMonth)
    {
        Array.Clear(_spawnDates);
        var count = SpawnRate;
        if (count <= 0)
        {
            return;
        }

        var month = simulation.Date.Month;
        if (!firstMonth)
        {
            month = (month % 12) + 1;
        }

        var lastDay = GameDate.DaysIn(month);
        var interval = (decimal)lastDay / count;
        var forceArrival = true;
        var targetDay = 1;
        while (targetDay <= lastDay)
        {
            targetDay += PoissonRandom(simulation.Random, interval);
            if (targetDay <= lastDay)
            {
                _spawnDates[targetDay]++;
                forceArrival = false;
            }
        }

        // Give the poor hospital a patient this month anyway.
        if (forceArrival)
        {
            _spawnDates[simulation.Random.NextInclusive(1, lastDay)] = 1;
        }

        PrepareSpawnHours(simulation);
    }

    /// <summary><c>World:_prepareSpawnHours</c>: gives the next day's patients random hours.</summary>
    private void PrepareSpawnHours(Simulation simulation)
    {
        Array.Clear(_spawnHours);
        var date = simulation.Date;
        var nextDay = date.IsLastDayOfMonth ? 1 : date.Day + 1;
        for (var i = 0; i < _spawnDates[nextDay]; i++)
        {
            _spawnHours[simulation.Random.NextInclusive(1, GameDate.HoursPerDay - 1)]++;
        }
    }

    /// <summary>
    /// A Poisson-distributed number with the given mean, by Knuth's method as <c>math.p_random</c>:
    /// multiply uniform numbers in (0, 1] until the product drops below e^−mean. Fixed point decimals keep
    /// it deterministic.
    /// </summary>
    public static int PoissonRandom(SimRandom random, decimal mean)
    {
        var limit = 1m / Exp(mean);
        var k = 0;
        var p = 1m;
        do
        {
            k++;
            p *= (random.Next(int.MaxValue) + 1m) / int.MaxValue;
        }
        while (p > limit);

        return k - 1;
    }

    /// <summary>e^x for 0 ≤ x ≤ 60 by its Taylor series.</summary>
    internal static decimal Exp(decimal x)
    {
        var sum = 1m;
        var term = 1m;
        for (var n = 1; n < 200; n++)
        {
            term = term * x / n;
            if (term < 0.0000000000000000001m)
            {
                break;
            }

            sum += term;
        }

        return sum;
    }

    /// <summary>
    /// <c>World:calculateSpawnTiles</c>: on each edge, the tiles from which the hospital can be reached, of which
    /// at most 8 spread evenly along the edge.
    /// </summary>
    internal static List<TilePosition> CalculateSpawnTiles(LevelMap map)
    {
        var reachable = ReachableFromHospital(map);
        var w = map.Width;
        var h = map.Height;
        var points = new List<TilePosition>();
        var edges = new (int X, int Y, int Dx, int Dy)[] { (0, 0, 1, 0), (w - 1, 0, 0, 1), (w - 1, h - 1, -1, 0), (0, h - 1, 0, -1) };
        foreach (var (startX, startY, dx, dy) in edges)
        {
            var candidates = new List<TilePosition>();
            for (int x = startX, y = startY; map.Contains(x, y); x += dx, y += dy)
            {
                if (reachable[(y * w) + x])
                {
                    candidates.Add(new TilePosition(x, y));
                }
            }

            var count = Math.Min(8, candidates.Count);
            for (var i = 1; i <= count; i++)
            {
                // math.floor((i - 0.5) / num * #xs + 1), 1-based.
                points.Add(candidates[((2 * i) - 1) * candidates.Count / (2 * count)]);
            }
        }

        return points;
    }

    /// <summary>Tiles from which a walk leads into the hospital: a flood fill backwards from its tiles.</summary>
    private static bool[] ReachableFromHospital(LevelMap map)
    {
        var w = map.Width;
        var reachable = new bool[w * map.Height];
        var queue = new Queue<TilePosition>();
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (map[x, y].Has(TileFlags.Hospital | TileFlags.Passable))
                {
                    reachable[(y * w) + x] = true;
                    queue.Enqueue(new TilePosition(x, y));
                }
            }
        }

        while (queue.Count > 0)
        {
            var tile = queue.Dequeue();
            foreach (var (dx, dy, back) in new[] { (0, -1, TileFlags.CanTravelSouth), (1, 0, TileFlags.CanTravelWest), (0, 1, TileFlags.CanTravelNorth), (-1, 0, TileFlags.CanTravelEast) })
            {
                var x = tile.X + dx;
                var y = tile.Y + dy;
                if (map.Contains(x, y) && !reachable[(y * w) + x] && map[x, y].Has(TileFlags.Passable | back))
                {
                    reachable[(y * w) + x] = true;
                    queue.Enqueue(new TilePosition(x, y));
                }
            }
        }

        return reachable;
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(SpawnRate);
        writer.WriteInt32(MonthlyIncrease);
        writer.WriteBool(HadPatients);
        foreach (var count in _spawnDates)
        {
            writer.WriteInt32(count);
        }

        foreach (var count in _spawnHours)
        {
            writer.WriteInt32(count);
        }
    }

    internal static PatientArrivals Read(SimReader reader)
    {
        var arrivals = new PatientArrivals
        {
            SpawnRate = reader.ReadInt32(),
            MonthlyIncrease = reader.ReadInt32(),
            HadPatients = reader.ReadBool(),
        };
        for (var i = 0; i < arrivals._spawnDates.Length; i++)
        {
            arrivals._spawnDates[i] = reader.ReadInt32();
        }

        for (var i = 0; i < arrivals._spawnHours.Length; i++)
        {
            arrivals._spawnHours[i] = reader.ReadInt32();
        }

        return arrivals;
    }
}

/// <summary>One entry of a level's <c>popn</c> setting: from the end of <paramref name="Month"/> on, the spawn rate changes by <paramref name="Change"/> each month.</summary>
public readonly record struct PopulationChange(int Month, int Change);
