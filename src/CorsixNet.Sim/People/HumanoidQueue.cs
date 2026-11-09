// Ported from CorsixTH CorsixTH/Lua/queue.lua and CorsixTH/Lua/humanoid_actions/queue.lua.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.People;

/// <summary>Someone who can stand in a <see cref="HumanoidQueue"/>.</summary>
internal interface IQueueMember
{
    /// <summary>Goes to a place in the queue, then turns to face <paramref name="face"/>.</summary>
    void TakeQueuePlace(Simulation simulation, Humanoid humanoid, TilePosition tile, TilePosition face);

    /// <summary>Whether they stand at their place in the queue, so that they may be seen.</summary>
    bool IsStandingInQueue(Humanoid humanoid);
}

/// <summary>
/// A queue of people waiting at a reception desk or a room's door, as CorsixTH's <c>Queue</c>. Members
/// stand on the idle tiles found from the queue's position: the first on the nearest, the next one
/// beside them and so on, each facing the one ahead.
/// </summary>
/// <remarks>
/// Of CorsixTH's priorities (people leaving, staff, guests, emergencies, then the other patients) only guests
/// (VIPs) come before patients yet, as only they queue with them. Benches come later.
/// </remarks>
public sealed class HumanoidQueue
{
    private const int MaxMembers = 1 << 16;

    private readonly List<int> _members = [];

    /// <param name="position">The tile the queue forms at: the desk's or door's use position.</param>
    /// <param name="face">The tile the first in line faces.</param>
    /// <param name="maxSize">From how many people on the queue is full.</param>
    public HumanoidQueue(TilePosition position, TilePosition face, int maxSize)
    {
        Position = position;
        Face = face;
        MaxSize = maxSize;
    }

    public TilePosition Position { get; }

    public TilePosition Face { get; }

    public int MaxSize { get; }

    /// <summary>The ids of the people in the queue, the front first.</summary>
    public IReadOnlyList<int> Members => _members;

    public int Count => _members.Count;

    public bool IsFull => _members.Count >= MaxSize;

    /// <summary>The id of the first in line, or 0.</summary>
    public int Front => _members.Count > 0 ? _members[0] : 0;

    /// <summary>How many people have been served here.</summary>
    public int VisitorCount { get; internal set; }

    public bool Contains(int id) => _members.Contains(id);

    /// <summary>
    /// Adds someone behind everyone of the same or a higher priority and sends them to their place
    /// (<c>Queue:push</c>).
    /// </summary>
    internal void Push(Simulation simulation, Humanoid humanoid)
    {
        var priority = Priority(humanoid);
        var index = _members.Count;
        while (index > 0 && Priority(simulation.FindEntity(_members[index - 1]) as Humanoid) > priority)
        {
            index--;
        }

        _members.Insert(index, humanoid.Id);
        Arrange(simulation);
    }

    /// <summary><c>_getHumanoidQueuePriority</c>: a smaller number goes first; VIPs (3) before patients (6).</summary>
    private static int Priority(Humanoid? humanoid) => humanoid?.Behaviour is VipBehaviour ? 3 : 6;

    /// <summary>Takes the first in line out; everyone moves up.</summary>
    internal int Pop(Simulation simulation)
    {
        var front = _members[0];
        _members.RemoveAt(0);
        Arrange(simulation);
        return front;
    }

    /// <summary>Takes someone out of the queue, wherever they are in it.</summary>
    internal bool Remove(Simulation simulation, int id)
    {
        if (!_members.Remove(id))
        {
            return false;
        }

        Arrange(simulation);
        return true;
    }

    /// <summary>
    /// Sends everyone to their place in line, as <c>action_queue_on_change_position</c> does for people
    /// standing: the n-th in line goes to the n-th idle tile and faces the tile of the one ahead.
    /// </summary>
    internal void Arrange(Simulation simulation)
    {
        for (var i = 0; i < _members.Count; i++)
        {
            if (simulation.FindEntity(_members[i]) is not Humanoid { Behaviour: IQueueMember member } humanoid
                || !simulation.Pathfinder.TryFindIdleTile(simulation.Map, Position, i, out var tile))
            {
                continue;
            }

            var face = Face;
            if (i > 0 && simulation.Pathfinder.TryFindIdleTile(simulation.Map, Position, i - 1, out var ahead))
            {
                face = ahead;
            }

            member.TakeQueuePlace(simulation, humanoid, tile, face);
        }
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteTile(Position);
        writer.WriteTile(Face);
        writer.WriteInt32(MaxSize);
        writer.WriteInt32(VisitorCount);
        writer.WriteInt32(_members.Count);
        foreach (var id in _members)
        {
            writer.WriteInt32(id);
        }
    }

    internal static HumanoidQueue Read(SimReader reader)
    {
        var queue = new HumanoidQueue(reader.ReadTile(), reader.ReadTile(), reader.ReadInt32()) { VisitorCount = reader.ReadInt32() };
        var count = reader.ReadCount(MaxMembers);
        for (var i = 0; i < count; i++)
        {
            queue._members.Add(reader.ReadInt32());
        }

        return queue;
    }
}
