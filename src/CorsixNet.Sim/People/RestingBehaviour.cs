// Ported from CorsixTH CorsixTH/Lua/humanoid_actions/seek_staffroom.lua, use_staffroom.lua and
// CorsixTH/Lua/rooms/staff_room.lua (StaffRoom:onHumanoidEnter).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Sim.People;

/// <summary>Where a resting member of staff is.</summary>
public enum RestingPhase : byte
{
    /// <summary>On the way to the staff room (<c>SeekStaffRoomAction</c>).</summary>
    WalkingToRoom,

    /// <summary>Walking to the sofa, pool table or video game picked.</summary>
    WalkingToObject,

    /// <summary>Using it, getting less tired every round.</summary>
    UsingObject,

    /// <summary>Nothing free to use: meandering two rounds before looking again.</summary>
    Meandering,

    /// <summary>Rested: out of the staff room, back to work.</summary>
    Leaving,
}

/// <summary>
/// A member of staff tired past the hospital's <c>goto_staffroom</c> policy, who has left their work for the nearest staff
/// room: there they sit on a sofa, play pool (doctors and handymen) or a video game (doctors and nurses), each round of
/// it taking some fatigue off, and switch now and then, until they are fully rested; then they go back to work (their
/// usual behaviour again, free for any room).
/// </summary>
public sealed class RestingBehaviour : HumanoidBehaviour
{
    private const string Sofa = "sofa";
    private const string PoolTable = "pool_table";
    private const string VideoGame = "video_game";

    internal RestingBehaviour(int roomId, int parcel)
    {
        RoomId = roomId;
        Parcel = parcel;
    }

    public override BehaviourKind Kind => BehaviourKind.Resting;

    public RestingPhase Phase { get; private set; }

    /// <summary>The staff room.</summary>
    public int RoomId { get; private set; }

    /// <summary>The object being walked to or used, reserved for this member of staff (<c>reserved_for</c>), or 0.</summary>
    public int TargetId { get; private set; }

    /// <summary>A handyman's parcel, given back afterwards.</summary>
    public int Parcel { get; }

    /// <summary>The use of the object, while it lasts.</summary>
    public ObjectUse? Use { get; private set; }

    private Meandering Meander { get; init; } = new();

    /// <summary>Rounds of the object left before thinking of another (<c>obj_use_time</c>).</summary>
    private int _useTime;

    /// <summary>The object picked to go to next once the current use ends, or 0 (<c>next_target_obj</c>).</summary>
    private int _nextTargetId;

    private int _meanders;

    internal override void Update(Simulation simulation, Humanoid humanoid)
    {
        if (simulation.FindEntity(RoomId) is not Room { IsActive: true } room || simulation.FindEntity(room.DoorId) is not RoomDoor door)
        {
            BackToWork(humanoid);
            return;
        }

        switch (Phase)
        {
            case RestingPhase.WalkingToRoom:
                if (!room.Area.Contains(humanoid.Tile))
                {
                    if (!humanoid.WalkTo(simulation, door.InsideTile(room.Area)))
                    {
                        BackToWork(humanoid);
                    }

                    return;
                }

                // StaffRoom:onHumanoidEnter: they count as a visitor and look for something to do.
                if (room.DoorQueue is { } queue)
                {
                    queue.VisitorCount++;
                }

                PickNext(simulation, humanoid, room, previousType: null);
                break;
            case RestingPhase.WalkingToObject:
                if (simulation.FindEntity(TargetId) is RoomObject target && humanoid.Tile == RoomVisits.UseTile(simulation, target))
                {
                    Phase = RestingPhase.UsingObject;
                    _useTime = UseTime(simulation, target.ObjectType);
                    Use = ObjectUse.Start(simulation, humanoid, target, prolonged: true, alwaysProlonged: true);
                }
                else
                {
                    PickNext(simulation, humanoid, room, previousType: null);
                }

                break;
            case RestingPhase.UsingObject:
                UseStep(simulation, humanoid, room, door);
                break;
            case RestingPhase.Meandering:
                Meander.Update(simulation, humanoid, loop: () =>
                {
                    if (--_meanders > 0)
                    {
                        return false;
                    }

                    PickNext(simulation, humanoid, room, previousType: null);
                    return true;
                });
                break;
            case RestingPhase.Leaving:
                if (room.Area.Contains(humanoid.Tile))
                {
                    if (!humanoid.WalkTo(simulation, RoomVisits.OutsideTile(room, door)))
                    {
                        BackToWork(humanoid);
                    }

                    return;
                }

                BackToWork(humanoid);
                break;
        }
    }

    /// <summary>
    /// <c>use_staffroom</c>'s loop callback: a round takes the object's relaxation off the fatigue; rested, they leave;
    /// otherwise, once the use time is up, they think of another object, and go to it if it is a different kind.
    /// </summary>
    private void UseStep(Simulation simulation, Humanoid humanoid, Room room, RoomDoor door)
    {
        var target = simulation.FindEntity(TargetId) as RoomObject;
        switch (Use?.Update(simulation, humanoid) ?? ObjectUseEvent.Done)
        {
            case ObjectUseEvent.Loop:
                humanoid.Fatigue = Math.Max(0, humanoid.Fatigue - Relaxation(target?.ObjectType));
                if (humanoid.Fatigue == 0)
                {
                    // Rested: get up, then leave.
                    Use!.Interrupt();
                    _nextTargetId = 0;
                    return;
                }

                if (--_useTime == 0)
                {
                    var next = Decide(simulation, humanoid, room, target?.ObjectType);
                    if (next is null || next.ObjectType == target?.ObjectType)
                    {
                        _useTime = UseTime(simulation, target?.ObjectType ?? Sofa);
                    }
                    else
                    {
                        _nextTargetId = next.Id;
                        Use!.Interrupt();
                    }
                }

                break;
            case ObjectUseEvent.Done:
                Use = null;
                if (Phase == RestingPhase.Leaving || humanoid.Fatigue == 0)
                {
                    TargetId = 0;
                    Phase = RestingPhase.Leaving;
                    humanoid.WalkTo(simulation, RoomVisits.OutsideTile(room, door));
                }
                else if (simulation.FindEntity(_nextTargetId) is RoomObject following)
                {
                    GoTo(simulation, humanoid, following);
                }
                else
                {
                    PickNext(simulation, humanoid, room, target?.ObjectType);
                }

                _nextTargetId = 0;
                break;
        }
    }

    private void PickNext(Simulation simulation, Humanoid humanoid, Room room, string? previousType)
    {
        Use = null;
        if (Decide(simulation, humanoid, room, previousType) is { } placed)
        {
            GoTo(simulation, humanoid, placed);
            return;
        }

        // Nothing free: MeanderAction():setCount(2), then look again.
        TargetId = 0;
        Phase = RestingPhase.Meandering;
        _meanders = 2;
        Meander.Reset();
    }

    private void GoTo(Simulation simulation, Humanoid humanoid, RoomObject placed)
    {
        TargetId = placed.Id;
        Phase = RestingPhase.WalkingToObject;
        humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, placed));
    }

    /// <summary>
    /// <c>decide_next_target</c>: usually the sofa; two times in ten a doctor or handyman picks the pool table, and two in
    /// ten a doctor or nurse the video game, unless that was the last thing used; the nearest free one of the kind.
    /// </summary>
    private RoomObject? Decide(Simulation simulation, Humanoid humanoid, Room room, string? current)
    {
        var chance = simulation.Random.NextInclusive(1, 10);
        var staffClass = humanoid.Profile?.Class;
        var type = Sofa;
        if (staffClass is StaffClass.Doctor or StaffClass.Handyman && current != PoolTable && chance <= 2)
        {
            type = PoolTable;
        }

        if (staffClass is StaffClass.Doctor or StaffClass.Nurse && current != VideoGame && chance is > 2 and <= 4)
        {
            type = VideoGame;
        }

        var taken = new HashSet<int>();
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Behaviour: RestingBehaviour resting } && entity.Id != humanoid.Id)
            {
                taken.Add(resting.TargetId);
                taken.Add(resting._nextTargetId);
            }
        }

        return simulation.Entities.OfType<RoomObject>()
            .Where(o => o.RoomId == room.Id && o.ObjectType == type && !taken.Contains(o.Id))
            .OrderBy(o => Math.Abs(o.Tile.X - humanoid.Tile.X) + Math.Abs(o.Tile.Y - humanoid.Tile.Y))
            .ThenBy(o => o.Id)
            .FirstOrDefault();
    }

    /// <summary><c>generate_use_time</c>: rounds on the sofa 50 to 80, at the pool table 2 to 5, at the video game 2 to 15.</summary>
    private static int UseTime(Simulation simulation, string type) => type switch
    {
        PoolTable => simulation.Random.NextInclusive(2, 5),
        VideoGame => simulation.Random.NextInclusive(2, 15),
        _ => simulation.Random.NextInclusive(50, 80),
    };

    /// <summary>The fatigue a round of the object takes off (<c>relaxation</c>).</summary>
    private static decimal Relaxation(string? type) => type is PoolTable or VideoGame ? 0.05m : 0.001m;

    /// <summary>Rested (or the staff room gone): back to their usual work, free for any room.</summary>
    private void BackToWork(Humanoid humanoid)
    {
        humanoid.Behaviour = StaffHiring.NewBehaviour(humanoid.Profile!.Class);
        if (humanoid.Behaviour is HandymanBehaviour handyman)
        {
            handyman.Parcel = Parcel;
        }
    }

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteInt32(RoomId);
        writer.WriteInt32(Parcel);
        writer.WriteByte((byte)Phase);
        writer.WriteInt32(TargetId);
        ObjectUse.WriteOptional(writer, Use);
        Meander.Write(writer);
        writer.WriteInt32(_useTime);
        writer.WriteInt32(_nextTargetId);
        writer.WriteInt32(_meanders);
    }

    internal static RestingBehaviour ReadState(SimReader reader)
    {
        var roomId = reader.ReadInt32();
        var parcel = reader.ReadInt32();
        var phase = (RestingPhase)reader.ReadByte();
        var target = reader.ReadInt32();
        var use = ObjectUse.ReadOptional(reader);
        var resting = new RestingBehaviour(roomId, parcel) { Phase = phase, Meander = Meandering.Read(reader) };
        resting.TargetId = target;
        resting.Use = use;
        resting._useTime = reader.ReadInt32();
        resting._nextTargetId = reader.ReadInt32();
        resting._meanders = reader.ReadInt32();
        return resting;
    }
}
