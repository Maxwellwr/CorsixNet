// Handymen, ported from CorsixTH CorsixTH/Lua/entities/humanoids/staff/handyman.lua (parcelNr, searchForHandymanTask,
// processCleaningTask, doMeandering), CorsixTH/Lua/hospital.lua (searchForHandymanTask, assignHandymanToTask),
// humanoid_actions/sweep_floor.lua, humanoid_actions/meander.lua (the handyman's parcel and his search for work),
// objects/litter.lua (the cleaning task), objects/plant.lua (callForWatering, createHandymanActions, getBestUsageTileXY,
// restoreToFullHealth, usage_animations), entities/machine.lua (createHandymanActions, getRepairTile,
// setHandymanRepairPosition, callHandymanForRepairIfNecessary) and dialogs/staff_dialog.lua (UIStaff:changeParcel).
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

    /// <summary>On his way to the tile he repairs a machine from.</summary>
    WalkingToMachine,

    /// <summary>Wandering about the machine's room until nobody uses it and no patient is in (the repair's meander).</summary>
    WaitingToRepair,

    /// <summary>Repairing the machine (<c>UseObjectAction(machine)</c>, at least 20 frames).</summary>
    Repairing,

    /// <summary>
    /// The machine is free: back to the tile he repairs it from. The room lets nobody in meanwhile, as the machine is his
    /// (CorsixTH's walk action before the repair).
    /// </summary>
    GoingToRepair,
}

/// <summary>The kinds of work a handyman does, which his priorities share out (<c>cleaning</c>, <c>watering</c>, <c>repairing</c>).</summary>
public enum HandymanTaskType : byte
{
    /// <summary>Sweeping litter.</summary>
    Cleaning,

    /// <summary>Watering plants.</summary>
    Watering,

    /// <summary>Repairing machines.</summary>
    Repairing,
}

/// <summary>
/// A handyman. Outside rooms he wanders about, within his assigned parcel (<c>parcelNr</c>) if he has one; at the start of
/// each round he looks for work: litter to sweep up, plants to water and worn machines to repair, nearest first, the more
/// urgent the sooner.
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
    private const int DefaultPriority = 333;

    /// <summary>A priority button's step up (<c>incr_value</c>), and the most the other two give up if they have it.</summary>
    private const int PriorityStep = 100;

    private const int SmallestDecrease = 50;

    private const int FullPriority = 1000;

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

    public override BehaviourKind Kind => BehaviourKind.Handyman;

    /// <summary>The parcel he keeps to, or 0 for all of them (<c>parcelNr</c>).</summary>
    public int Parcel { get; internal set; }

    /// <summary>How much of his time goes to sweeping litter, in thousandths (the <c>cleaning</c> attribute).</summary>
    public int CleaningPriority { get; private set; } = DefaultPriority;

    /// <summary>How much of his time goes to watering plants, in thousandths (the <c>watering</c> attribute).</summary>
    public int WateringPriority { get; private set; } = DefaultPriority;

    /// <summary>How much of his time goes to repairing machines, in thousandths (the <c>repairing</c> attribute).</summary>
    public int RepairingPriority { get; private set; } = DefaultPriority;

    /// <summary>His priority for a kind of work, in thousandths.</summary>
    public int Priority(HandymanTaskType type) => type switch
    {
        HandymanTaskType.Cleaning => CleaningPriority,
        HandymanTaskType.Watering => WateringPriority,
        _ => RepairingPriority,
    };

    /// <summary>
    /// <c>UIStaff:changeHandymanAttributes</c>: one kind of work goes up a tenth (to at most all of his time, when the
    /// others go to nothing); the other two give up that tenth less the smaller of a twentieth and the least of them,
    /// each at least nothing.
    /// </summary>
    internal void IncreasePriority(HandymanTaskType increased)
    {
        var step = PriorityStep;
        var smallest = SmallestDecrease;
        foreach (var type in Enum.GetValues<HandymanTaskType>())
        {
            if (type == increased)
            {
                SetPriority(type, Math.Min(FullPriority, Priority(type) + step));
                if (Priority(type) == FullPriority)
                {
                    step = 2 * FullPriority;
                }
            }
            else
            {
                smallest = Math.Min(smallest, Priority(type));
            }
        }

        var decrease = step - smallest;
        foreach (var type in Enum.GetValues<HandymanTaskType>())
        {
            if (type != increased)
            {
                SetPriority(type, Math.Max(0, Priority(type) - decrease));
            }
        }
    }

    private void SetPriority(HandymanTaskType type, int value)
    {
        switch (type)
        {
            case HandymanTaskType.Cleaning:
                CleaningPriority = value;
                break;
            case HandymanTaskType.Watering:
                WateringPriority = value;
                break;
            default:
                RepairingPriority = value;
                break;
        }
    }

    public HandymanPhase Phase { get; private set; }

    /// <summary>The litter, plant or machine he is going to or working on, or 0 (<c>task.object</c>).</summary>
    public int TargetId { get; private set; }

    private Meandering Meander { get; init; } = new();

    /// <summary>Ticks left of the current animation.</summary>
    private int _timer;

    /// <summary>Which animation of sweeping, watering or repairing is showing, from 0.</summary>
    private int _stage;

    /// <summary>Rounds of the repair's in-use animation still to show after the current one.</summary>
    private int _rounds;

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
            case HandymanPhase.WalkingToMachine:
            case HandymanPhase.WaitingToRepair:
            case HandymanPhase.GoingToRepair:
                if (simulation.FindEntity(TargetId) is not RoomObject { Machine: not null, IsDestroyed: false } machine
                    || simulation.FindEntity(machine.RoomId) is not Room { IsCrashed: false } room
                    || RepairTile(simulation, machine) is not { } repairTile)
                {
                    AnswerCall(simulation, humanoid);
                    return;
                }

                if (Phase == HandymanPhase.GoingToRepair || ReadyForRepair(simulation, room, machine, humanoid))
                {
                    if (humanoid.Tile == repairTile)
                    {
                        if (room.PatientId == 0 && !RoomVisits.IsObjectBusy(simulation, machine, humanoid))
                        {
                            StartRepair(simulation, humanoid);
                        }
                        else
                        {
                            // The last patient is still on the way out.
                            Phase = HandymanPhase.GoingToRepair;
                        }
                    }
                    else if (!humanoid.WalkTo(simulation, repairTile))
                    {
                        AnswerCall(simulation, humanoid);
                    }
                    else
                    {
                        Phase = HandymanPhase.GoingToRepair;
                    }

                    return;
                }

                if (Phase == HandymanPhase.WalkingToMachine)
                {
                    Phase = HandymanPhase.WaitingToRepair;
                    Meander.Reset();
                }

                Meander.Update(simulation, humanoid);
                break;
            case HandymanPhase.Repairing:
                if (--_timer > 0)
                {
                    return;
                }

                RepairStep(simulation, humanoid);
                break;
        }
    }

    /// <summary>
    /// The repair's meander ends once nobody uses the machine and the room has no patient, or only one on the way out
    /// (<c>meander_loop_callback</c>).
    /// </summary>
    private static bool ReadyForRepair(Simulation simulation, Room room, RoomObject machine, Humanoid humanoid) =>
        (room.PatientId == 0 || simulation.FindEntity(room.PatientId) is Humanoid { Behaviour: PatientBehaviour { Phase: PatientPhase.LeavingRoom } })
        && !RoomVisits.IsObjectBusy(simulation, machine, humanoid);

    /// <summary>
    /// <c>getRepairTile</c> and <c>setHandymanRepairPosition</c>: the machine's handyman position, the first of several in
    /// its room that can be reached from its use position; without one, its use position.
    /// </summary>
    internal static TilePosition? RepairTile(Simulation simulation, RoomObject machine)
    {
        if (simulation.Content.FindObject(machine.ObjectType) is not { } definition
            || !definition.Orientations.TryGetValue(machine.Orientation, out var layout))
        {
            return null;
        }

        var use = new TilePosition(machine.Tile.X + layout.Use.X, machine.Tile.Y + layout.Use.Y);
        if (definition.Machine?.HandymanPositions?.GetValueOrDefault(machine.Orientation) is not { Count: > 0 } positions)
        {
            return use;
        }

        if (positions.Count == 1)
        {
            return new TilePosition(machine.Tile.X + positions[0].X, machine.Tile.Y + positions[0].Y);
        }

        foreach (var (x, y) in positions)
        {
            var tile = new TilePosition(machine.Tile.X + x, machine.Tile.Y + y);
            if (simulation.Map.Contains(tile.X, tile.Y) && RoomAt(simulation, tile) == machine.RoomId
                && simulation.Pathfinder.FindDistance(simulation.Map, tile, use) is not null)
            {
                return tile;
            }
        }

        return null;
    }

    /// <summary>
    /// <c>UseObjectAction(machine):setProlongedUsage(false)</c> with <c>min_length = 20</c>: the begin animation, the in-use
    /// one as many times as makes 20 frames, then the finish one; drawn where a patient's use is, moved by the handyman's
    /// offset, and mirrored facing east and west.
    /// </summary>
    private void StartRepair(Simulation simulation, Humanoid humanoid)
    {
        Phase = HandymanPhase.Repairing;
        _stage = -1;
        _rounds = 0;
        RepairStep(simulation, humanoid);
    }

    private void RepairStep(Simulation simulation, Humanoid humanoid)
    {
        if (simulation.FindEntity(TargetId) is not RoomObject { Machine: not null, IsDestroyed: false } machine
            || simulation.Content.FindObject(machine.ObjectType) is not { Machine: { } definition } objectDefinition
            || !objectDefinition.Orientations.TryGetValue(machine.Orientation, out var layout))
        {
            humanoid.ClearActionAnimation();
            AnswerCall(simulation, humanoid);
            return;
        }

        var repair = definition.Repair;
        AnimationRef? next = null;
        while (next is null && _stage < 4)
        {
            if (_stage == 2 && _rounds > 0)
            {
                _rounds--;
                next = PickInUse(simulation, repair);
                break;
            }

            _stage++;
            switch (_stage)
            {
                case 0:
                    next = repair.Begin;
                    break;
                case 1:
                    next = repair.Begin2;
                    break;
                case 2:
                    if (repair.InUse.Count > 0)
                    {
                        next = PickInUse(simulation, repair);
                        var length = next.Value.Length;
                        _rounds = length > 0 ? ((MachineDefinition.MinimumRepairFrames + length - 1) / length) - 1 : 0;
                    }

                    break;
                case 3:
                    next = repair.Finish;
                    break;
                case 4:
                    next = repair.Finish2;
                    break;
            }
        }

        if (next is not { } animation)
        {
            // machineRepaired, then AnswerCallAction.
            humanoid.ClearActionAnimation();
            Objects.Machines.Repaired(simulation, machine);
            TargetId = 0;
            AnswerCall(simulation, humanoid);
            return;
        }

        var offset = definition.HandymanOffsets?.GetValueOrDefault(machine.Orientation) ?? default;
        TilePosition tile, drawTile;
        if (layout.AnimateFromUsePosition)
        {
            tile = drawTile = new TilePosition(humanoid.Tile.X + offset.X, humanoid.Tile.Y + offset.Y);
        }
        else
        {
            tile = new TilePosition(machine.Tile.X + offset.X, machine.Tile.Y + offset.Y);
            drawTile = new TilePosition(machine.Tile.X + layout.RenderAttach.X + offset.X, machine.Tile.Y + layout.RenderAttach.Y + offset.Y);
        }

        var flip = machine.Orientation is Direction.East or Direction.West;
        var hidden = objectDefinition.VisibleWhileUsed ? 0 : machine.Id;
        humanoid.SetActionAnimation(simulation, animation, flip, tile, drawTile, hidden);
        _timer = animation.Length;
    }

    private static AnimationRef PickInUse(Simulation simulation, UseAnimations repair) =>
        repair.InUse.Count == 1 ? repair.InUse[0] : repair.InUse[simulation.Random.Next(repair.InUse.Count)];

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
        HandymanTaskType first, second, third;
        if (nr < CleaningPriority)
        {
            (first, second, third) = (HandymanTaskType.Cleaning, HandymanTaskType.Watering, HandymanTaskType.Repairing);
        }
        else if (nr < CleaningPriority + WateringPriority)
        {
            (first, second, third) = (HandymanTaskType.Watering, HandymanTaskType.Cleaning, HandymanTaskType.Repairing);
        }
        else
        {
            (first, second, third) = (HandymanTaskType.Repairing, HandymanTaskType.Watering, HandymanTaskType.Cleaning);
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

    /// <summary>A task: the litter or plant, where he goes to deal with it, and how urgent it is.</summary>
    private readonly record struct HandymanTask(HandymanTaskType Type, Entity Target, TilePosition Tile, int Priority);

    /// <summary>
    /// <c>Hospital:searchForHandymanTask</c>: the first reachable task of the type in his parcel, unless another is more
    /// urgent or nearer; one within three tiles is taken at once. A task another handyman is on is his only if he is
    /// more than five tiles nearer to it, counting a consultant's distance as half and a junior's as double.
    /// </summary>
    private static HandymanTask? FindTask(Simulation simulation, Humanoid humanoid, HandymanTaskType type)
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
    /// same room (<c>getBestUsageTileXY</c>), unless someone waters them already; machines with fewer than 6 uses left, more
    /// urgently below 4, unless a handyman is at them already.
    /// </summary>
    private static IEnumerable<HandymanTask> Tasks(Simulation simulation, Humanoid humanoid, HandymanTaskType type)
    {
        foreach (var entity in simulation.Entities)
        {
            switch (type)
            {
                case HandymanTaskType.Cleaning when entity is Litter { IsRemoved: false, IsCleanable: true } litter && litter.Player == humanoid.Employer
                    && !IsBeingSwept(simulation, litter.Id):
                    yield return new HandymanTask(type, litter, litter.Tile, 1);
                    break;
                case HandymanTaskType.Watering when entity is RoomObject { Plant: { NeedsWatering: true } state } plant
                    && Owner(simulation, plant.Tile) == humanoid.Employer && BestUsageTile(simulation, humanoid, plant) is { } tile:
                    yield return new HandymanTask(type, plant, tile, state.State + 1);
                    break;
                case HandymanTaskType.Repairing when entity is RoomObject { Machine: { } wear, IsDestroyed: false } machine
                    && (wear.RemainingUses < 6 || wear.IsRepairCalled)
                    && Owner(simulation, machine.Tile) == humanoid.Employer
                    && simulation.FindEntity(machine.RoomId) is Room { IsActive: true, IsCrashed: false }
                    && !IsBeingRepaired(simulation, machine.Id) && RepairTile(simulation, machine) is { } repairTile:
                    // callHandymanForRepairIfNecessary: below 6 uses left a task of priority 1, below 4 of priority 2; the machine
                    // window's call (UIMachine:callHandyman) is of priority 2.
                    yield return new HandymanTask(type, machine, repairTile, wear.RemainingUses < 4 || wear.IsRepairCalled ? 2 : 1);
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
        Phase = task.Type switch
        {
            HandymanTaskType.Cleaning => HandymanPhase.WalkingToLitter,
            HandymanTaskType.Watering => HandymanPhase.WalkingToPlant,
            _ => HandymanPhase.WalkingToMachine,
        };
        Meander.Reset();
        return true;
    }

    /// <summary>The other handyman on his way to the task, if any.</summary>
    private static Humanoid? AssignedHandyman(Simulation simulation, int targetId, Humanoid self)
    {
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Behaviour: HandymanBehaviour { Phase: HandymanPhase.WalkingToLitter or HandymanPhase.WalkingToPlant or HandymanPhase.WalkingToMachine } other, IsFired: false } handyman
                && handyman.Id != self.Id && other.TargetId == targetId)
            {
                return handyman;
            }
        }

        return null;
    }

    /// <summary>Whether a handyman waits at the machine or repairs it (the task is his then).</summary>
    internal static bool IsBeingRepaired(Simulation simulation, int machineId) =>
        simulation.Entities.Any(e => e is Humanoid { Behaviour: HandymanBehaviour { Phase: HandymanPhase.WaitingToRepair or HandymanPhase.GoingToRepair or HandymanPhase.Repairing } other }
            && other.TargetId == machineId);

    /// <summary>Whether a handyman is repairing a machine in the room or about to, which then lets nobody in.</summary>
    internal static bool IsRepairingIn(Simulation simulation, Room room) =>
        simulation.Entities.Any(e => e is RoomObject { Machine.IsRepairCalled: true, IsDestroyed: false } called && called.RoomId == room.Id)
        || simulation.Entities.Any(e => e is Humanoid { Behaviour: HandymanBehaviour { Phase: HandymanPhase.GoingToRepair or HandymanPhase.Repairing } other }
            && simulation.FindEntity(other.TargetId) is RoomObject machine && machine.RoomId == room.Id);

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
        writer.WriteInt32(_rounds);
        writer.WriteInt32(CleaningPriority);
        writer.WriteInt32(WateringPriority);
        writer.WriteInt32(RepairingPriority);
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

        if (reader.Version is 0 or >= 68)
        {
            handyman._rounds = reader.ReadInt32();
        }

        if (reader.Version is 0 or >= 78)
        {
            handyman.CleaningPriority = reader.ReadInt32();
            handyman.WateringPriority = reader.ReadInt32();
            handyman.RepairingPriority = reader.ReadInt32();
        }

        return handyman;
    }
}
