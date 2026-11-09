// Handymen, ported from CorsixTH CorsixTH/Lua/entities/humanoids/staff/handyman.lua (parcelNr, searchForHandymanTask,
// processCleaningTask, doMeandering), CorsixTH/Lua/hospital.lua (searchForHandymanTask, assignHandymanToTask),
// humanoid_actions/sweep_floor.lua, humanoid_actions/meander.lua (the handyman's parcel and his search for work),
// objects/litter.lua (the cleaning task), objects/plant.lua (callForWatering, createHandymanActions, getBestUsageTileXY,
// restoreToFullHealth, usage_animations) and dialogs/staff_dialog.lua (UIStaff:changeParcel).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.People;

/// <summary>Where a handyman is in his work.</summary>
public enum HandymanPhase : byte
{
    /// <summary>Wandering about, looking for work at the start of each round.</summary>
    Meandering,

    WalkingToLitter,

    /// <summary>Taking the broom out, sweeping and putting it back (<c>SweepFloorAction</c>).</summary>
    Sweeping,

    WalkingToPlant,

    /// <summary>Watering the plant (<c>UseObjectAction(plant):enableWateringPlant()</c>).</summary>
    Watering,

    /// <summary>Done in a room with nothing more to do: out of its door, then wandering (<c>doMeandering</c>).</summary>
    LeavingRoom,
}

/// <summary>
/// A handyman. Outside rooms he wanders about, within his assigned parcel (<c>parcelNr</c>) if he has one; at the start of
/// each round he looks for work: litter to sweep up and plants to water, nearest first, the more urgent the sooner. Machines
/// do not break down yet, so there is nothing to repair.
/// </summary>
/// <remarks>
/// CorsixTH keeps a list of handyman tasks in the hospital, added when litter is dropped or a plant calls for water. Here
/// the tasks are worked out from the litter and plants themselves, in entity order, which is the order CorsixTH adds them
/// in; the task a handyman is on is the one he is walking to. CorsixTH stops a handyman walking to litter that is gone
/// before he gets there; here he finds out when he arrives.
/// </remarks>
public sealed class HandymanBehaviour : HumanoidBehaviour
{
    /// <summary>The handyman's default priorities (<c>Handyman:setProfile</c>): a third each, in thousandths.</summary>
    private const int CleaningPriority = 333;

    private const int WateringPriority = 333;

    private const int RepairingPriority = 333;

    /// <summary>A task nearer than this is taken at once (<c>thresholdForStopping</c>).</summary>
    private const int ThresholdForStopping = 3;

    /// <summary>Taking the broom out (sweep_floor.lua's first animation).</summary>
    private static readonly AnimationRef TakeBroomOut = new(1874, 20);

    /// <summary>Sweeping (its second animation, shown twice before the litter goes and twice after).</summary>
    private static readonly AnimationRef Sweep = new(1878, 4);

    /// <summary>Plant usage: <c>begin_use</c> and <c>in_use</c> facing north and east; west and south mirror them.</summary>
    private static readonly AnimationRef WaterBeginNorth = new(1972, 25);

    private static readonly AnimationRef WaterBeginEast = new(1974, 45);

    private static readonly AnimationRef WaterNorth = new(1980, 15);

    private static readonly AnimationRef WaterEast = new(1982, 16);

    private enum TaskType : byte
    {
        Cleaning,
        Watering,
        Repairing,
    }

    public override BehaviourKind Kind => BehaviourKind.Handyman;

    /// <summary>The parcel he keeps to, or 0 for all of them (<c>parcelNr</c>).</summary>
    public int Parcel { get; internal set; }

    public HandymanPhase Phase { get; private set; }

    /// <summary>The litter or plant he is going to or working on, or 0 (<c>task.object</c>).</summary>
    public int TargetId { get; private set; }

    private Meandering Meander { get; init; } = new();

    /// <summary>Ticks left of the current animation.</summary>
    private int _timer;

    /// <summary>Which animation of sweeping or watering is showing, from 0.</summary>
    private int _stage;

    internal override void Update(Simulation simulation, Humanoid humanoid)
    {
        switch (Phase)
        {
            case HandymanPhase.Meandering:
                // meander_action_start: outside rooms, each round starts with a look for work.
                var inRoom = RoomAt(simulation, humanoid.Tile) != 0;
                if (!inRoom && Meander.AtRoundStart && SearchForTask(simulation, humanoid))
                {
                    return;
                }

                // Handymen are free to visit any room, so the parcel holds only outside rooms.
                Meander.Update(simulation, humanoid, inRoom ? 0 : Parcel);
                break;
            case HandymanPhase.WalkingToLitter:
                if (simulation.FindEntity(TargetId) is not Litter { IsRemoved: false } litter || humanoid.Tile != litter.Tile)
                {
                    AnswerCall(simulation, humanoid);
                    return;
                }

                // action_sweep_floor_start: the task is his for good now.
                Phase = HandymanPhase.Sweeping;
                _stage = 0;
                _timer = TakeBroomOut.Length;
                humanoid.SetActionAnimation(simulation, TakeBroomOut, flip: false, humanoid.Tile, humanoid.Tile, hiddenObject: 0);
                break;
            case HandymanPhase.Sweeping:
                if (--_timer > 0)
                {
                    return;
                }

                SweepStep(simulation, humanoid);
                break;
            case HandymanPhase.WalkingToPlant:
                if (simulation.FindEntity(TargetId) is not RoomObject { Plant: { } } plant || !IsNextTo(humanoid.Tile, plant.Tile))
                {
                    AnswerCall(simulation, humanoid);
                    return;
                }

                StartWatering(simulation, humanoid, plant);
                break;
            case HandymanPhase.Watering:
                if (--_timer > 0)
                {
                    return;
                }

                WaterStep(simulation, humanoid);
                break;
            case HandymanPhase.LeavingRoom:
                Phase = HandymanPhase.Meandering;
                Meander.Reset();
                break;
        }
    }

    /// <summary>
    /// The broom: out (one round of its animation), two rounds of sweeping and the litter is gone, two more and he is done
    /// (<c>action_sweep_floor_sweep</c>, <c>action_sweep_floor_remove_litter</c>, <c>action_sweep_floor_finish</c>).
    /// </summary>
    private void SweepStep(Simulation simulation, Humanoid humanoid)
    {
        _stage++;
        switch (_stage)
        {
            case 1:
                humanoid.SetActionAnimation(simulation, Sweep, flip: false, humanoid.Tile, humanoid.Tile, hiddenObject: 0);
                _timer = Sweep.Length * 2;
                break;
            case 2:
                if (simulation.FindEntity(TargetId) is Litter litter)
                {
                    litter.Remove();
                }

                TargetId = 0;
                _timer = Sweep.Length * 2;
                break;
            default:
                humanoid.ClearActionAnimation();
                AnswerCall(simulation, humanoid);
                break;
        }
    }

    /// <summary>The plant is watered back to health as he starts (<c>restoreToFullHealth</c>); he waters it once through.</summary>
    private void StartWatering(Simulation simulation, Humanoid humanoid, RoomObject plant)
    {
        plant.Plant!.Restore();
        Phase = HandymanPhase.Watering;
        _stage = 0;
        humanoid.FaceTowards(plant.Tile);
        var (animation, flip) = WaterAnimation(humanoid.Facing, begin: true);
        _timer = animation.Length;
        humanoid.SetActionAnimation(simulation, animation, flip, humanoid.Tile, humanoid.Tile, hiddenObject: 0);
    }

    private void WaterStep(Simulation simulation, Humanoid humanoid)
    {
        _stage++;
        if (_stage == 1)
        {
            var (animation, flip) = WaterAnimation(humanoid.Facing, begin: false);
            _timer = animation.Length;
            humanoid.SetActionAnimation(simulation, animation, flip, humanoid.Tile, humanoid.Tile, hiddenObject: 0);
            return;
        }

        humanoid.ClearActionAnimation();
        TargetId = 0;
        AnswerCall(simulation, humanoid);
    }

    private static (AnimationRef Animation, bool Flip) WaterAnimation(Direction facing, bool begin) => facing switch
    {
        Direction.North => (begin ? WaterBeginNorth : WaterNorth, false),
        Direction.West => (begin ? WaterBeginNorth : WaterNorth, true),
        Direction.East => (begin ? WaterBeginEast : WaterEast, false),
        _ => (begin ? WaterBeginEast : WaterEast, true),
    };

    /// <summary>
    /// <c>AnswerCallAction</c>: the next task straight away; with none, out of the room he is in, then wandering
    /// (<c>doMeandering</c>).
    /// </summary>
    private void AnswerCall(Simulation simulation, Humanoid humanoid)
    {
        TargetId = 0;
        Phase = HandymanPhase.Meandering;
        Meander.Reset();
        if (SearchForTask(simulation, humanoid))
        {
            return;
        }

        if (simulation.FindEntity(RoomAt(simulation, humanoid.Tile)) is Room room && simulation.FindEntity(room.DoorId) is RoomDoor door
            && humanoid.WalkTo(simulation, RoomVisits.OutsideTile(room, door)))
        {
            Phase = HandymanPhase.LeavingRoom;
        }
    }

    /// <summary>Gives up the task he was on, which another handyman has taken (<c>releaseHandymanFromTask</c>).</summary>
    private void Release(Simulation simulation, Humanoid humanoid)
    {
        humanoid.StopWalking();
        AnswerCall(simulation, humanoid);
    }

    /// <summary>
    /// <c>Handyman:searchForHandymanTask</c>: the type of work he looks for first is picked at random by his priorities, then
    /// the others; he goes to the task found.
    /// </summary>
    /// <returns>Whether he has a task now.</returns>
    private bool SearchForTask(Simulation simulation, Humanoid humanoid)
    {
        var nr = simulation.Random.Next(1000);
        TaskType first, second, third;
        if (nr < CleaningPriority)
        {
            (first, second, third) = (TaskType.Cleaning, TaskType.Watering, TaskType.Repairing);
        }
        else if (nr < CleaningPriority + WateringPriority)
        {
            (first, second, third) = (TaskType.Watering, TaskType.Cleaning, TaskType.Repairing);
        }
        else
        {
            (first, second, third) = (TaskType.Repairing, TaskType.Watering, TaskType.Cleaning);
        }

        if (FindTask(simulation, humanoid, first) is { } task)
        {
            return Assign(simulation, humanoid, task);
        }

        if (Priority(first) >= 1000)
        {
            return false;
        }

        var sum = Priority(second) + Priority(third);
        if (simulation.Random.NextInclusive(0, sum / 10) > Priority(second) / 10)
        {
            (second, third) = (third, second);
        }

        if (FindTask(simulation, humanoid, second) is { } secondTask)
        {
            return Assign(simulation, humanoid, secondTask);
        }

        return Priority(third) > 0 && FindTask(simulation, humanoid, third) is { } thirdTask && Assign(simulation, humanoid, thirdTask);
    }

    private static int Priority(TaskType type) => type switch
    {
        TaskType.Cleaning => CleaningPriority,
        TaskType.Watering => WateringPriority,
        _ => RepairingPriority,
    };

    /// <summary>A task: the litter or plant, where he goes to deal with it, and how urgent it is.</summary>
    private readonly record struct HandymanTask(TaskType Type, Entity Target, TilePosition Tile, int Priority);

    /// <summary>
    /// <c>Hospital:searchForHandymanTask</c>: the first reachable task of the type in his parcel, unless another is more
    /// urgent or nearer; one within three tiles is taken at once. A task another handyman is on is his only if he is
    /// more than five tiles nearer to it, counting a consultant's distance as half and a junior's as double.
    /// </summary>
    private static HandymanTask? FindTask(Simulation simulation, Humanoid humanoid, TaskType type)
    {
        var multiplier = humanoid.Profile switch
        {
            { IsConsultant: true } => 0.5m,
            { IsJunior: true } => 2m,
            _ => 1m,
        };
        HandymanTask? best = null;
        var bestDistance = 0m;
        var bestPriority = 0;
        foreach (var task in Tasks(simulation, humanoid, type))
        {
            if (best is not null && task.Priority < bestPriority)
            {
                continue;
            }

            var map = simulation.Map;
            var parcel = map[task.Tile.X, task.Tile.Y].Parcel;
            var own = humanoid.Behaviour as HandymanBehaviour;
            if (own!.Parcel != 0 && own.Parcel != parcel)
            {
                continue;
            }

            if (simulation.Pathfinder.FindDistance(map, humanoid.StepTarget, task.Tile) is not { } found)
            {
                continue;
            }

            decimal distance = found;
            if (AssignedHandyman(simulation, task.Target.Id, humanoid) is { } assigned
                && simulation.Pathfinder.FindDistance(map, assigned.StepTarget, task.Tile) is { } assignedFound)
            {
                decimal assignedDistance = assignedFound;
                if (assigned.Profile is { IsConsultant: true })
                {
                    assignedDistance /= 2;
                }
                else if (assigned.Profile is { IsJunior: true })
                {
                    assignedDistance *= 2;
                }

                if ((distance * multiplier) + 5 > assignedDistance)
                {
                    continue;
                }
            }

            if (best is null)
            {
                if (distance <= ThresholdForStopping)
                {
                    return task;
                }

                (best, bestDistance, bestPriority) = (task, distance, task.Priority);
            }
            else if (bestPriority < task.Priority || distance < bestDistance)
            {
                if (distance < ThresholdForStopping)
                {
                    return task;
                }

                (best, bestDistance, bestPriority) = (task, distance, task.Priority);
            }
        }

        return best;
    }

    /// <summary>
    /// The tasks of a type in his hospital: cleanable litter (priority 1, <c>Litter:setLitterType</c>); thirsty plants,
    /// the thirstier the more urgent (<c>Plant:callForWatering</c>), from the nearest side he can reach them from in the
    /// same room (<c>getBestUsageTileXY</c>), unless someone waters them already. Nothing breaks down yet to repair.
    /// </summary>
    private static IEnumerable<HandymanTask> Tasks(Simulation simulation, Humanoid humanoid, TaskType type)
    {
        foreach (var entity in simulation.Entities)
        {
            switch (type)
            {
                case TaskType.Cleaning when entity is Litter { IsRemoved: false, IsCleanable: true } litter && litter.Player == humanoid.Employer
                    && !IsBeingSwept(simulation, litter.Id):
                    yield return new HandymanTask(type, litter, litter.Tile, 1);
                    break;
                case TaskType.Watering when entity is RoomObject { Plant: { NeedsWatering: true } state } plant
                    && Owner(simulation, plant.Tile) == humanoid.Employer && BestUsageTile(simulation, humanoid, plant) is { } tile:
                    yield return new HandymanTask(type, plant, tile, state.State + 1);
                    break;
            }
        }
    }

    /// <summary>Takes the task, from another handyman if he had it (<c>assignHandymanToTask</c>), and goes to it.</summary>
    private bool Assign(Simulation simulation, Humanoid humanoid, HandymanTask task)
    {
        if (AssignedHandyman(simulation, task.Target.Id, humanoid) is { Behaviour: HandymanBehaviour former } formerHandyman)
        {
            former.Release(simulation, formerHandyman);
        }

        if (!humanoid.WalkTo(simulation, task.Tile))
        {
            return false;
        }

        TargetId = task.Target.Id;
        Phase = task.Type == TaskType.Cleaning ? HandymanPhase.WalkingToLitter : HandymanPhase.WalkingToPlant;
        Meander.Reset();
        return true;
    }

    /// <summary>The other handyman on his way to the task, if any.</summary>
    private static Humanoid? AssignedHandyman(Simulation simulation, int targetId, Humanoid self)
    {
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Behaviour: HandymanBehaviour { Phase: HandymanPhase.WalkingToLitter or HandymanPhase.WalkingToPlant } other, IsFired: false } handyman
                && handyman.Id != self.Id && other.TargetId == targetId)
            {
                return handyman;
            }
        }

        return null;
    }

    /// <summary>Whether a handyman has started sweeping the litter (the task is gone then).</summary>
    private static bool IsBeingSwept(Simulation simulation, int litterId) =>
        simulation.Entities.Any(e => e is Humanoid { Behaviour: HandymanBehaviour { Phase: HandymanPhase.Sweeping } other } && other.TargetId == litterId);

    /// <summary>
    /// <c>Plant:getBestUsageTileXY</c>: of the four tiles beside the plant, those in the same room (or corridor) and in the
    /// hospital, the one nearest to the handyman by path; null if none can be reached.
    /// </summary>
    private static TilePosition? BestUsageTile(Simulation simulation, Humanoid humanoid, RoomObject plant)
    {
        var map = simulation.Map;
        var room = RoomAt(simulation, plant.Tile);
        TilePosition? best = null;
        var shortest = 0;
        foreach (var (dx, dy) in new[] { (0, 1), (0, -1), (-1, 0), (1, 0) })
        {
            var tile = new TilePosition(plant.Tile.X + dx, plant.Tile.Y + dy);
            if (!map.Contains(tile.X, tile.Y) || RoomAt(simulation, tile) != room || !Disasters.Epidemics.IsInHospital(simulation, humanoid.Employer, tile))
            {
                continue;
            }

            if (simulation.Pathfinder.FindDistance(map, humanoid.StepTarget, tile) is { } distance && (best is null || distance < shortest))
            {
                best = tile;
                shortest = distance;
            }
        }

        return best;
    }

    private static bool IsNextTo(TilePosition a, TilePosition b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1;

    private static int RoomAt(Simulation simulation, TilePosition tile) =>
        simulation.Map.Contains(tile.X, tile.Y) ? simulation.Map[tile.X, tile.Y].RoomId : 0;

    private static int Owner(Simulation simulation, TilePosition tile) =>
        simulation.Map.Contains(tile.X, tile.Y) ? simulation.Map.GetParcelOwner(simulation.Map[tile.X, tile.Y].Parcel) - 1 : -1;

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteInt32(Parcel);
        Meander.Write(writer);
        writer.WriteByte((byte)Phase);
        writer.WriteInt32(TargetId);
        writer.WriteInt32(_timer);
        writer.WriteInt32(_stage);
    }

    internal static HandymanBehaviour ReadState(SimReader reader)
    {
        var handyman = new HandymanBehaviour { Parcel = reader.ReadInt32(), Meander = Meandering.Read(reader) };
        if (reader.Version is 0 or >= 58)
        {
            handyman.Phase = (HandymanPhase)reader.ReadByte();
            handyman.TargetId = reader.ReadInt32();
            handyman._timer = reader.ReadInt32();
            handyman._stage = reader.ReadInt32();
        }

        return handyman;
    }
}
