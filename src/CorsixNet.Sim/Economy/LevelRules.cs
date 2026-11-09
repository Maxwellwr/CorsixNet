using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Disasters;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Serialization;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Sim.Economy;

/// <summary>
/// Whether an object type can be had on a level at all (the level files' <c>objects[n].AvailableForLevel</c>) and
/// whether from the start, without research (<c>objects[n].StartAvail</c>).
/// </summary>
public readonly record struct ObjectAvailability(bool ForLevel, bool AtStart);

/// <summary>
/// The money settings of a level, from its level files: starting cash, what rooms and objects
/// cost, and the staff settings (salaries, who is available for hire). Unlike <see cref="GameContent"/>, they differ between levels, so they are part of the
/// saved state.
/// </summary>
/// <remarks>
/// A room's cost here is its own cost (the level files' <c>rooms[n].Cost</c>), without the objects
/// it needs; CorsixTH's <c>build_cost</c> is this plus the needed objects.
/// </remarks>
public sealed class LevelRules
{
    /// <summary>CorsixTH's base_config.lua: 4 patients the first month, then one more each month, capped from month 27.</summary>
    /// <remarks>Declared before <see cref="Free"/>, which uses it.</remarks>
    private static readonly PopulationChange[] DefaultPopulation = [new(0, 4), new(1, 1), new(27, 0)];

    /// <summary>No money at the start and everything free, staff included.</summary>
    public static readonly LevelRules Free = new(0, [], [], StaffRules.Free);

    /// <summary>CorsixTH's base_config.lua <c>payroll.MaxSalary</c>.</summary>
    public const int DefaultMaxSalary = 2000;

    /// <summary>CorsixTH's base_config.lua <c>gbv.MayorLaunch</c>.</summary>
    public const int DefaultMayorLaunch = 150;

    /// <summary>CorsixTH's fallback for <c>gbv.LandCostPerTile</c> (map.lua's <c>Map:getParcelPrice</c>).</summary>
    public const int DefaultLandCostPerTile = 25;

    private const int MaxEntries = 1024;

    private readonly (string Id, int Cost)[] _roomCosts;
    private readonly (string Id, int Cost)[] _objectCosts;
    private readonly Dictionary<string, int> _roomLookup;
    private readonly Dictionary<string, int> _objectLookup;
    private readonly (string Id, ObjectAvailability Availability)[] _objectAvailability;
    private readonly Dictionary<string, ObjectAvailability> _availabilityLookup;

    /// <param name="startCash">Each hospital's balance at the start.</param>
    /// <param name="roomCosts">Cost of each room type by room id, without its objects.</param>
    /// <param name="objectCosts">Cost of each object type by object id.</param>
    /// <param name="staff">Salaries and staff for hire; by default the original game's settings.</param>
    /// <param name="population">How the number of patients per month changes; by default CorsixTH's base_config.lua.</param>
    /// <param name="diseases">Which diseases come and their prices; by default <see cref="DiseaseRules.Default"/>.</param>
    /// <param name="startReputation">Each hospital's reputation at the start (the level files' <c>StartRep</c>).</param>
    /// <param name="awards">Trophy and award settings; by default <see cref="AwardRules.Default"/>.</param>
    /// <param name="landCostPerTile">What a tile of land is worth (<c>gbv.LandCostPerTile</c>).</param>
    /// <param name="endConditions">The goals to win and lose by; by default none.</param>
    /// <param name="interestRate">The bank's yearly interest in hundredths of a percent (<c>InterestRate</c>).</param>
    /// <param name="overdraftDifferential">What overdrafts cost on top, likewise (<c>OverdraftDiff</c>).</param>
    /// <param name="objectAvailability">Which object types the level has and from when; an object it does not name is there from the start.</param>
    /// <param name="earthquakes">The level's planned earthquakes in order (<c>quake_control</c>); by default none.</param>
    /// <param name="maxSalary">The highest salary (<c>payroll.MaxSalary</c>); staff earning it are always happy.</param>
    /// <param name="tiredness">When staff count as tired; by default <see cref="TirednessRules.Default"/>.</param>
    /// <param name="mayorLaunch">Days on average between VIP visits (<c>gbv.MayorLaunch</c>).</param>
    public LevelRules(
        int startCash,
        IEnumerable<KeyValuePair<string, int>> roomCosts,
        IEnumerable<KeyValuePair<string, int>> objectCosts,
        StaffRules? staff = null,
        IEnumerable<PopulationChange>? population = null,
        DiseaseRules? diseases = null,
        int startReputation = Hospital.AverageReputation,
        AwardRules? awards = null,
        int landCostPerTile = DefaultLandCostPerTile,
        EndConditions? endConditions = null,
        int interestRate = Hospital.DefaultInterestRate,
        int overdraftDifferential = Hospital.DefaultOverdraftDifferential,
        IEnumerable<KeyValuePair<string, ObjectAvailability>>? objectAvailability = null,
        IEnumerable<PlannedEarthquake>? earthquakes = null,
        int maxSalary = DefaultMaxSalary,
        TirednessRules? tiredness = null,
        int mayorLaunch = DefaultMayorLaunch)
    {
        MayorLaunch = mayorLaunch;
        Tiredness = tiredness ?? TirednessRules.Default;
        MaxSalary = maxSalary;
        Earthquakes = [.. earthquakes ?? []];
        InterestRate = interestRate;
        OverdraftDifferential = overdraftDifferential;
        EndConditions = endConditions ?? EndConditions.None;
        Awards = awards ?? AwardRules.Default;
        LandCostPerTile = landCostPerTile;
        StartReputation = startReputation;
        Diseases = diseases ?? DiseaseRules.Default;
        StartCash = startCash;
        Staff = staff ?? StaffRules.Default;
        Population = [.. population ?? DefaultPopulation];
        _roomCosts = Sorted(roomCosts);
        _objectCosts = Sorted(objectCosts);
        _roomLookup = _roomCosts.ToDictionary(entry => entry.Id, entry => entry.Cost, StringComparer.Ordinal);
        _objectLookup = _objectCosts.ToDictionary(entry => entry.Id, entry => entry.Cost, StringComparer.Ordinal);
        _objectAvailability = [.. (objectAvailability ?? []).Select(pair => (pair.Key, pair.Value)).OrderBy(entry => entry.Key, StringComparer.Ordinal)];
        _availabilityLookup = _objectAvailability.ToDictionary(entry => entry.Id, entry => entry.Availability, StringComparer.Ordinal);
    }

    public int StartCash { get; }

    /// <summary>Each hospital's reputation at the start, from 0 to 1000.</summary>
    public int StartReputation { get; }

    /// <summary>What it takes to win trophies and awards at the year end, and what they bring.</summary>
    public AwardRules Awards { get; }

    /// <summary>What a tile of land is worth, for the hospital's value.</summary>
    public int LandCostPerTile { get; }

    /// <summary>The goals to win and lose the level by.</summary>
    public EndConditions EndConditions { get; }

    /// <summary>The bank's yearly interest on loans, in hundredths of a percent.</summary>
    public int InterestRate { get; }

    /// <summary>What an overdraft costs a year on top of the interest, in hundredths of a percent.</summary>
    public int OverdraftDifferential { get; }

    /// <summary>Salaries and who is available for hire when.</summary>
    public StaffRules Staff { get; }

    /// <summary>
    /// The level's <c>popn</c> entries in file order: patients in the first month, then from which month on the number
    /// changes by how much each month.
    /// </summary>
    public IReadOnlyList<PopulationChange> Population { get; }

    /// <summary>Which diseases the level has, from when, and their prices.</summary>
    public DiseaseRules Diseases { get; }

    /// <summary>The highest salary: staff earning it are never unhappy.</summary>
    public int MaxSalary { get; }

    /// <summary>The level's planned earthquakes, in the order they come.</summary>
    public IReadOnlyList<PlannedEarthquake> Earthquakes { get; }

    /// <summary>When staff count as tired, very tired and crack-up tired.</summary>
    public TirednessRules Tiredness { get; }

    /// <summary>Days on average between VIP visits (<c>gbv.MayorLaunch</c>).</summary>
    public int MayorLaunch { get; }

    /// <summary>What a room of this type costs, without its objects; 0 if the level does not say.</summary>
    public int RoomCost(string roomId) => _roomLookup.GetValueOrDefault(roomId);

    /// <summary>What one object of this type costs; 0 if the level does not say.</summary>
    public int ObjectCost(string objectId) => _objectLookup.GetValueOrDefault(objectId);

    /// <summary>What the objects a room needs cost together.</summary>
    public int NeededObjectsCost(RoomDefinition room)
    {
        var cost = 0;
        foreach (var (objectId, count) in Needed(room))
        {
            cost += count * ObjectCost(objectId);
        }

        return cost;
    }

    /// <summary>
    /// What building a room costs in all: the room and the objects it needs. A hospital needs this
    /// much to start a room (CorsixTH's <c>build_cost</c>).
    /// </summary>
    public int BuildCost(RoomDefinition room) => RoomCost(room.Id) + NeededObjectsCost(room);

    /// <summary>Whether the level has this object type, and from the start; one the level does not name is there from the start.</summary>
    public ObjectAvailability Availability(string objectId) => _availabilityLookup.GetValueOrDefault(objectId, new ObjectAvailability(true, true));

    /// <summary>
    /// Whether a room of this type can ever be built on the level: every object it needs must be available for the
    /// level (CorsixTH's <c>World:getAvailableRooms</c>).
    /// </summary>
    public bool IsRoomAvailable(RoomDefinition room) => Needed(room).All(needed => Availability(needed.ObjectId).ForLevel);

    /// <summary>
    /// Whether a room of this type can be built from the start: it is available and every object it needs is there
    /// from the start; otherwise it has to be researched first (CorsixTH's <c>is_discovered</c>).
    /// </summary>
    public bool IsRoomDiscoveredAtStart(RoomDefinition room) => Needed(room).All(needed => Availability(needed.ObjectId) is { ForLevel: true, AtStart: true });

    /// <summary>The objects a room needs, in id order.</summary>
    public static IEnumerable<(string ObjectId, int Count)> Needed(RoomDefinition room)
    {
        return (room.NeededObjects ?? new Dictionary<string, int>())
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (pair.Key, pair.Value));
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(StartCash);
        writer.WriteInt32(StartReputation);
        writer.WriteInt32(LandCostPerTile);
        Awards.Write(writer);
        EndConditions.Write(writer);
        writer.WriteInt32(InterestRate);
        writer.WriteInt32(OverdraftDifferential);
        WriteCosts(writer, _roomCosts);
        WriteCosts(writer, _objectCosts);
        writer.WriteInt32(_objectAvailability.Length);
        foreach (var (id, availability) in _objectAvailability)
        {
            writer.WriteString(id);
            writer.WriteBool(availability.ForLevel);
            writer.WriteBool(availability.AtStart);
        }

        Staff.Write(writer);
        writer.WriteInt32(Population.Count);
        foreach (var entry in Population)
        {
            writer.WriteInt32(entry.Month);
            writer.WriteInt32(entry.Change);
        }

        Diseases.Write(writer);
        writer.WriteInt32(Earthquakes.Count);
        foreach (var earthquake in Earthquakes)
        {
            writer.WriteInt32(earthquake.StartMonth);
            writer.WriteInt32(earthquake.EndMonth);
            writer.WriteInt32(earthquake.Severity);
        }

        writer.WriteInt32(MaxSalary);
        Tiredness.Write(writer);
        writer.WriteInt32(MayorLaunch);
    }

    internal static LevelRules Read(SimReader reader)
    {
        var startCash = reader.ReadInt32();
        var startReputation = reader.ReadInt32();
        var landCostPerTile = reader.ReadInt32();
        var awards = AwardRules.Read(reader);
        var endConditions = EndConditions.Read(reader);
        var interestRate = reader.ReadInt32();
        var overdraftDifferential = reader.ReadInt32();
        var rooms = ReadCosts(reader);
        var objects = ReadCosts(reader);
        var availability = new KeyValuePair<string, ObjectAvailability>[reader.ReadCount(MaxEntries)];
        for (var i = 0; i < availability.Length; i++)
        {
            availability[i] = new(reader.ReadString(), new ObjectAvailability(reader.ReadBool(), reader.ReadBool()));
        }

        var staff = StaffRules.Read(reader);
        var population = new PopulationChange[reader.ReadCount(MaxEntries)];
        for (var i = 0; i < population.Length; i++)
        {
            population[i] = new PopulationChange(reader.ReadInt32(), reader.ReadInt32());
        }

        var diseases = DiseaseRules.Read(reader);
        var earthquakes = new PlannedEarthquake[reader.ReadCount(MaxEntries)];
        for (var i = 0; i < earthquakes.Length; i++)
        {
            earthquakes[i] = new PlannedEarthquake(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        }

        return new LevelRules(startCash, rooms, objects, staff, population, diseases, startReputation, awards, landCostPerTile, endConditions, interestRate, overdraftDifferential, availability, earthquakes, reader.ReadInt32(), TirednessRules.Read(reader), reader.ReadInt32());
    }

    private static (string Id, int Cost)[] Sorted(IEnumerable<KeyValuePair<string, int>> costs)
    {
        var sorted = costs.Select(pair => (pair.Key, pair.Value)).OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        for (var i = 1; i < sorted.Length; i++)
        {
            if (string.Equals(sorted[i].Key, sorted[i - 1].Key, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Cost of '{sorted[i].Key}' is given twice.", nameof(costs));
            }
        }

        return sorted;
    }

    private static void WriteCosts(SimWriter writer, (string Id, int Cost)[] costs)
    {
        writer.WriteInt32(costs.Length);
        foreach (var (id, cost) in costs)
        {
            writer.WriteString(id);
            writer.WriteInt32(cost);
        }
    }

    private static List<KeyValuePair<string, int>> ReadCosts(SimReader reader)
    {
        var count = reader.ReadCount(MaxEntries);
        var costs = new List<KeyValuePair<string, int>>(count);
        for (var i = 0; i < count; i++)
        {
            costs.Add(new(reader.ReadString(), reader.ReadInt32()));
        }

        return costs;
    }
}
