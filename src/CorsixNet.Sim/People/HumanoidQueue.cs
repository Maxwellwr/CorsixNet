// Ported from CorsixTH CorsixTH/Lua/queue.lua and CorsixTH/Lua/humanoid_actions/queue.lua.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Economy;
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
/// (VIPs) and emergencies come before other patients yet, as only they queue with them.
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

    public int MaxSize { get; private set; }

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
        var priority = Priority(simulation, humanoid);
        var index = _members.Count;
        while (index > 0 && Priority(simulation, simulation.FindEntity(_members[index - 1]) as Humanoid) > priority)
        {
            index--;
        }

        _members.Insert(index, humanoid.Id);
        Arrange(simulation);
    }

    /// <summary>
    /// <c>_getHumanoidQueuePriority</c>: a smaller number goes first; VIPs and the inspector (3), then emergency patients (4), then,
    /// with the queue jump cheat on, patients with less than 0.10 health (5), then the other patients (6).
    /// </summary>
    private static int Priority(Simulation simulation, Humanoid? humanoid) => humanoid?.Behaviour switch
    {
        VipBehaviour or InspectorBehaviour => 3,
        PatientBehaviour { EmergencyIndex: > 0 } => 4,
        PatientBehaviour { Health: < 0.10m } when humanoid.HospitalPlayer >= 0
            && humanoid.HospitalPlayer < simulation.Hospitals.Count
            && (simulation.Hospitals[humanoid.HospitalPlayer].ActiveCheats & ToggleCheats.QueueJump) != 0 => 5,
        _ => 6,
    };

    /// <summary>The longest a queue window can make a queue (<c>Queue:increaseMaxSize</c>).</summary>
    public const int MaxMaxSize = 30;

    /// <summary>
    /// <c>Queue:increaseMaxSize</c> and <c>decreaseMaxSize</c>: the queue window's buttons make the queue longer or shorter,
    /// from 0 to 30 people; a full queue only puts off newcomers.
    /// </summary>
    internal void ChangeMaxSize(int amount) => MaxSize = amount < 0 ? Math.Max(0, MaxSize + amount) : Math.Min(MaxMaxSize, MaxSize + amount);

    /// <summary>Where someone stands in the queue, 0 at the front, or −1.</summary>
    internal int IndexOf(int id) => _members.IndexOf(id);

    /// <summary>
    /// <c>Queue:movePatient</c> and <c>Queue:move</c>: the queue window moves someone from <paramref name="index"/> to
    /// <paramref name="newIndex"/>, whatever their priority; those in between move up or down a place.
    /// </summary>
    internal void Move(Simulation simulation, int index, int newIndex)
    {
        if (index < 0 || index >= _members.Count || newIndex < 0 || newIndex >= _members.Count || index == newIndex)
        {
            return;
        }

        var id = _members[index];
        _members.RemoveAt(index);
        _members.Insert(newIndex, id);
        Arrange(simulation);
    }

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
        var threshold = BenchThreshold(simulation);
        var standing = 0;
        for (var i = 0; i < _members.Count; i++)
        {
            if (simulation.FindEntity(_members[i]) is not Humanoid { Behaviour: IQueueMember member } humanoid)
            {
                continue;
            }

            if (member is PatientBehaviour patient)
            {
                if (TrySit(simulation, humanoid, patient, i >= threshold))
                {
                    continue;
                }

                patient.LeaveBench(simulation, humanoid);
            }

            if (!simulation.Pathfinder.TryFindIdleTile(simulation.Map, Position, standing, out var tile))
            {
                standing++;
                continue;
            }

            var face = Face;
            if (standing > 0 && simulation.Pathfinder.TryFindIdleTile(simulation.Map, Position, standing - 1, out var ahead))
            {
                face = ahead;
            }

            standing++;
            member.TakeQueuePlace(simulation, humanoid, tile, face);
        }
    }

    /// <summary>Path distance from the goal within which a patient on a bench stays put (<c>DISTANCE_THRESHOLD</c>).</summary>
    private const int BenchKeepDistance = 4;

    /// <summary>How far from the goal a standing patient looks for a bench (<c>MAX_DISTANCE</c>).</summary>
    private const int BenchMaxDistance = 10;

    /// <summary>
    /// <c>action_queue_on_change_position</c>'s benches: a patient who may sit (not an alien, whose disease must_stand,
    /// and not among the first <c>bench_threshold</c>) keeps a bench within 4 tiles of the goal, or takes a free bench
    /// nearer the goal: within 10 tiles while standing, within two thirds of their own distance (at least 4) while
    /// sitting further off.
    /// </summary>
    /// <returns>Whether the patient sits or goes to sit, so that they take no standing place.</returns>
    private bool TrySit(Simulation simulation, Humanoid humanoid, PatientBehaviour patient, bool maySit)
    {
        if (!maySit || patient.DiseaseId == "alien_dna" || patient.Phase is not (PatientPhase.InReceptionQueue or PatientPhase.InDoorQueue)
            || (patient.BenchId == 0 && patient.IsSidetracked))
        {
            return false;
        }

        var sitting = patient.BenchId != 0;
        if (sitting && patient.BenchDistance is >= 0 and <= BenchKeepDistance)
        {
            return true;
        }

        int look;
        if (sitting)
        {
            if (simulation.Pathfinder.FindDistance(simulation.Map, humanoid.StepTarget, Position) is not { } fromGoal)
            {
                return true;
            }

            look = Math.Max((2 * fromGoal + 2) / 3, BenchKeepDistance);
        }
        else
        {
            look = BenchMaxDistance;
        }

        if (FreeBench(simulation, look) is { } found)
        {
            patient.GoToBench(simulation, humanoid, found.Bench, found.Distance);
            return true;
        }

        return sitting;
    }

    /// <summary>
    /// <c>World:getFreeBench</c>: the corridor bench nearest the goal by path whose use position is within
    /// <paramref name="distance"/>, that nobody sits on or goes to.
    /// </summary>
    private (RoomObject Bench, int Distance)? FreeBench(Simulation simulation, int distance)
    {
        (RoomObject Bench, int Distance)? best = null;
        foreach (var entity in simulation.Entities)
        {
            if (entity is not RoomObject { ObjectType: "bench", RoomId: 0, IsDestroyed: false } bench)
            {
                continue;
            }

            if (simulation.Entities.Any(e => e is Humanoid { Behaviour: PatientBehaviour other } && other.BenchId == bench.Id))
            {
                continue;
            }

            if (simulation.Pathfinder.FindDistance(simulation.Map, Position, RoomVisits.UseTile(simulation, bench)) is { } found
                && found <= distance && (best is null || found < best.Value.Distance))
            {
                best = (bench, found);
            }
        }

        return best;
    }

    /// <summary>
    /// <c>Queue:setBenchThreshold</c>: reception desks (reception_desk.lua) and the toilets (<c>ToiletRoom:roomFinished</c>)
    /// keep their first three in line standing; other rooms' queues none.
    /// </summary>
    private int BenchThreshold(Simulation simulation)
    {
        foreach (var entity in simulation.Entities)
        {
            switch (entity)
            {
                case Room room when ReferenceEquals(room.DoorQueue, this):
                    return room.RoomType == RoomVisits.ToiletsRoom ? 3 : 0;
                case RoomObject { Desk: { } desk } when ReferenceEquals(desk.Queue, this):
                    return 3;
            }
        }

        return 0;
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
