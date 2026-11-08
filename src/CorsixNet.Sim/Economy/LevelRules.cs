using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Serialization;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Sim.Economy;

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

    private const int MaxEntries = 1024;

    private readonly (string Id, int Cost)[] _roomCosts;
    private readonly (string Id, int Cost)[] _objectCosts;
    private readonly Dictionary<string, int> _roomLookup;
    private readonly Dictionary<string, int> _objectLookup;

    /// <param name="startCash">Each hospital's balance at the start.</param>
    /// <param name="roomCosts">Cost of each room type by room id, without its objects.</param>
    /// <param name="objectCosts">Cost of each object type by object id.</param>
    /// <param name="staff">Salaries and staff for hire; by default the original game's settings.</param>
    /// <param name="population">How the number of patients per month changes; by default CorsixTH's base_config.lua.</param>
    /// <param name="diseases">Which diseases come and their prices; by default <see cref="DiseaseRules.Default"/>.</param>
    /// <param name="startReputation">Each hospital's reputation at the start (the level files' <c>StartRep</c>).</param>
    public LevelRules(
        int startCash,
        IEnumerable<KeyValuePair<string, int>> roomCosts,
        IEnumerable<KeyValuePair<string, int>> objectCosts,
        StaffRules? staff = null,
        IEnumerable<PopulationChange>? population = null,
        DiseaseRules? diseases = null,
        int startReputation = Hospital.AverageReputation)
    {
        StartReputation = startReputation;
        Diseases = diseases ?? DiseaseRules.Default;
        StartCash = startCash;
        Staff = staff ?? StaffRules.Default;
        Population = [.. population ?? DefaultPopulation];
        _roomCosts = Sorted(roomCosts);
        _objectCosts = Sorted(objectCosts);
        _roomLookup = _roomCosts.ToDictionary(entry => entry.Id, entry => entry.Cost, StringComparer.Ordinal);
        _objectLookup = _objectCosts.ToDictionary(entry => entry.Id, entry => entry.Cost, StringComparer.Ordinal);
    }

    public int StartCash { get; }

    /// <summary>Each hospital's reputation at the start, from 0 to 1000.</summary>
    public int StartReputation { get; }

    /// <summary>Salaries and who is available for hire when.</summary>
    public StaffRules Staff { get; }

    /// <summary>
    /// The level's <c>popn</c> entries in file order: patients in the first month, then from which month on the number
    /// changes by how much each month.
    /// </summary>
    public IReadOnlyList<PopulationChange> Population { get; }

    /// <summary>Which diseases the level has, from when, and their prices.</summary>
    public DiseaseRules Diseases { get; }

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
        WriteCosts(writer, _roomCosts);
        WriteCosts(writer, _objectCosts);
        Staff.Write(writer);
        writer.WriteInt32(Population.Count);
        foreach (var entry in Population)
        {
            writer.WriteInt32(entry.Month);
            writer.WriteInt32(entry.Change);
        }

        Diseases.Write(writer);
    }

    internal static LevelRules Read(SimReader reader)
    {
        var startCash = reader.ReadInt32();
        var startReputation = reader.ReadInt32();
        var rooms = ReadCosts(reader);
        var objects = ReadCosts(reader);
        var staff = StaffRules.Read(reader);
        var population = new PopulationChange[reader.ReadCount(MaxEntries)];
        for (var i = 0; i < population.Length; i++)
        {
            population[i] = new PopulationChange(reader.ReadInt32(), reader.ReadInt32());
        }

        return new LevelRules(startCash, rooms, objects, staff, population, DiseaseRules.Read(reader), startReputation);
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
