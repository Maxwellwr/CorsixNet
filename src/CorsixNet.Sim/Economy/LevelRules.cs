using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Economy;

/// <summary>
/// The money settings of a level, from its level files: starting cash and what rooms and objects
/// cost. Unlike <see cref="GameContent"/>, they differ between levels, so they are part of the
/// saved state.
/// </summary>
/// <remarks>
/// A room's cost here is its own cost (the level files' <c>rooms[n].Cost</c>), without the objects
/// it needs; CorsixTH's <c>build_cost</c> is this plus the needed objects.
/// </remarks>
public sealed class LevelRules
{
    /// <summary>No money at the start and everything free.</summary>
    public static readonly LevelRules Free = new(0, [], []);

    private const int MaxEntries = 1024;

    private readonly (string Id, int Cost)[] _roomCosts;
    private readonly (string Id, int Cost)[] _objectCosts;
    private readonly Dictionary<string, int> _roomLookup;
    private readonly Dictionary<string, int> _objectLookup;

    /// <param name="startCash">Each hospital's balance at the start.</param>
    /// <param name="roomCosts">Cost of each room type by room id, without its objects.</param>
    /// <param name="objectCosts">Cost of each object type by object id.</param>
    public LevelRules(int startCash, IEnumerable<KeyValuePair<string, int>> roomCosts, IEnumerable<KeyValuePair<string, int>> objectCosts)
    {
        StartCash = startCash;
        _roomCosts = Sorted(roomCosts);
        _objectCosts = Sorted(objectCosts);
        _roomLookup = _roomCosts.ToDictionary(entry => entry.Id, entry => entry.Cost, StringComparer.Ordinal);
        _objectLookup = _objectCosts.ToDictionary(entry => entry.Id, entry => entry.Cost, StringComparer.Ordinal);
    }

    public int StartCash { get; }

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
        WriteCosts(writer, _roomCosts);
        WriteCosts(writer, _objectCosts);
    }

    internal static LevelRules Read(SimReader reader)
    {
        var startCash = reader.ReadInt32();
        var rooms = ReadCosts(reader);
        var objects = ReadCosts(reader);
        return new LevelRules(startCash, rooms, objects);
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
