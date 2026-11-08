// Ported from CorsixTH CorsixTH/Lua/humanoid_actions/seek_reception.lua, seek_room.lua, queue.lua and
// spawn.lua, CorsixTH/Lua/rooms/gp.lua (commandEnteringPatient) and CorsixTH/Lua/entities/humanoids/patient.lua.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.People;

/// <summary>Where a patient is in their visit.</summary>
public enum PatientPhase : byte
{
    /// <summary>Looking for a reception desk.</summary>
    SeekReception,

    /// <summary>Walking towards a desk until close enough to join its queue.</summary>
    WalkingToReception,

    InReceptionQueue,

    /// <summary>Looking for a room of the type they were sent to.</summary>
    SeekRoom,

    /// <summary>Walking to the room's door.</summary>
    WalkingToRoom,

    InDoorQueue,

    /// <summary>Let in: walking through the door.</summary>
    EnteringRoom,

    /// <summary>Walking to the object they use in the room, such as the GP's chair.</summary>
    WalkingToObject,

    UsingObject,

    /// <summary>Walking out of the room's door.</summary>
    LeavingRoom,

    /// <summary>Walking to the edge of the map to leave.</summary>
    GoingHome,
}

/// <summary>
/// A patient's visit: find a reception desk and queue there; when the receptionist has seen them, go to
/// the GP's office, queue at its door, sit down on the chair until the doctor is done, then go home.
/// </summary>
/// <remarks>
/// Diseases, diagnosis and treatment come later; for now every patient goes home after the GP.
/// </remarks>
public sealed class PatientBehaviour : HumanoidBehaviour
{
    /// <summary>The room type a patient goes to after reception.</summary>
    public const string FirstRoom = "gp";

    /// <summary>How many days a patient waits for a reception desk or a room before going home.</summary>
    private const int WaitingDays = 5;

    public override BehaviourKind Kind => BehaviourKind.Patient;

    public PatientPhase Phase { get; private set; }

    /// <summary>The desk or room the patient is heading for or queueing at, or 0.</summary>
    public int TargetId { get; private set; }

    /// <summary>The tile of the patient's place in a queue.</summary>
    public TilePosition QueueTile { get; private set; }

    /// <summary>The tile the patient faces when at their place in a queue.</summary>
    public TilePosition QueueFace { get; private set; }

    /// <summary>The patient's use of an object in a room, while it lasts.</summary>
    public ObjectUse? Use { get; private set; }

    /// <summary>Days the patient still waits for a missing desk or room (CorsixTH's <c>waiting</c>); −1 when not waiting.</summary>
    private int Waiting { get; set; } = -1;

    /// <summary>Where the patient leaves the map.</summary>
    private TilePosition HomeTile { get; set; }

    /// <summary>Whether the patient stands at their place in the queue, so that the desk may see them.</summary>
    public bool IsStandingInQueue(Humanoid humanoid) => !humanoid.IsMoving && humanoid.Tile == QueueTile;

    /// <summary>Whether the patient sits on the room's object, being seen (CorsixTH's <c>user_of</c>).</summary>
    public bool IsBeingSeen => Phase == PatientPhase.UsingObject && Use is { IsUsing: true };

    internal override void Update(Simulation simulation, Humanoid humanoid)
    {
        switch (Phase)
        {
            case PatientPhase.SeekReception:
            case PatientPhase.WalkingToReception:
                SeekReception(simulation, humanoid);
                break;
            case PatientPhase.InReceptionQueue:
            case PatientPhase.InDoorQueue:
                if (humanoid.Tile == QueueTile)
                {
                    humanoid.FaceTowards(QueueFace);
                }

                break;
            case PatientPhase.SeekRoom:
                SeekRoom(simulation, humanoid);
                break;
            case PatientPhase.WalkingToRoom:
                ArriveAtRoom(simulation, humanoid);
                break;
            case PatientPhase.EnteringRoom:
                EnterRoom(simulation, humanoid);
                break;
            case PatientPhase.WalkingToObject:
                if (RoomVisits.PatientObject(simulation, TargetId) is { } placed)
                {
                    Phase = PatientPhase.UsingObject;
                    Use = ObjectUse.Start(simulation, humanoid, placed);
                }
                else
                {
                    LeaveRoom(simulation, humanoid);
                }

                break;
            case PatientPhase.UsingObject:
                if (Use is null || Use.Update(simulation, humanoid) == ObjectUseEvent.Done)
                {
                    LeaveRoom(simulation, humanoid);
                }

                break;
            case PatientPhase.LeavingRoom:
                if (simulation.FindEntity(TargetId) is Room room && room.PatientId == humanoid.Id)
                {
                    room.PatientId = 0;
                }

                GoHome(simulation, humanoid);
                break;
            case PatientPhase.GoingHome:
                if (humanoid.Tile == HomeTile || !humanoid.WalkTo(simulation, HomeTile))
                {
                    humanoid.IsDespawning = true;
                }

                break;
        }
    }

    /// <summary>The receptionist has seen the patient: on to the GP.</summary>
    internal void OnSeenAtReception()
    {
        Phase = PatientPhase.SeekRoom;
        TargetId = 0;
        Waiting = -1;
    }

    /// <summary>Goes to a place in a queue, then turns to face <paramref name="face"/>.</summary>
    internal void TakeQueuePlace(Simulation simulation, Humanoid humanoid, TilePosition tile, TilePosition face)
    {
        QueueFace = face;
        if (QueueTile == tile && (humanoid.Tile == tile || humanoid.Destination == tile))
        {
            return;
        }

        QueueTile = tile;
        if (humanoid.Tile != tile || humanoid.IsMoving)
        {
            humanoid.WalkTo(simulation, tile);
        }
    }

    /// <summary>The room lets the patient in from its queue.</summary>
    internal void LetIn(Simulation simulation, Humanoid humanoid, Room room, RoomDoor door)
    {
        Phase = PatientPhase.EnteringRoom;
        TargetId = room.Id;
        humanoid.WalkTo(simulation, door.InsideTile(room.Area));
    }

    /// <summary>The doctor is done with the patient: they get up and leave (CorsixTH's <c>dealtWithPatient</c>).</summary>
    internal void DealtWith() => Use?.Interrupt();

    /// <summary>
    /// <c>action_seek_reception_start</c>: picks the best desk; close to it, the patient joins its queue,
    /// otherwise they walk towards it until they are. Without a desk they wait a while, then go home.
    /// </summary>
    private void SeekReception(Simulation simulation, Humanoid humanoid)
    {
        if (ReceptionDesks.FindBest(simulation, humanoid) is not var (desk, distance))
        {
            Phase = PatientPhase.SeekReception;
            WaitOrGoHome(simulation, humanoid);
            return;
        }

        Waiting = -1;
        TargetId = desk.Id;
        if (ReceptionDesks.CanJoinQueueAt(simulation, humanoid, humanoid.Tile, distance))
        {
            Phase = PatientPhase.InReceptionQueue;
            desk.Desk!.Queue.Push(simulation, humanoid);
            return;
        }

        // Walk towards the desk, but stop as soon as the queue may be joined (trimQueuingTail).
        var path = new List<TilePosition>();
        if (!simulation.Pathfinder.TryFindPath(simulation.Map, humanoid.Tile, ReceptionDesks.UseTile(simulation, desk), path))
        {
            WaitOrGoHome(simulation, humanoid);
            return;
        }

        var last = path.Count - 1;
        for (var i = last - 1; i >= 1; i--)
        {
            if (!ReceptionDesks.CanJoinQueueAt(simulation, humanoid, path[i], last - i))
            {
                break;
            }

            path.RemoveAt(i + 1);
        }

        Phase = PatientPhase.WalkingToReception;
        humanoid.FollowPath(path);
    }

    /// <summary>
    /// <c>action_seek_room_start</c>: picks the best room of the type, by distance and how busy it is, and
    /// walks to its door. Without one the patient waits a while, then goes home.
    /// </summary>
    private void SeekRoom(Simulation simulation, Humanoid humanoid)
    {
        if (RoomVisits.FindBest(simulation, humanoid, FirstRoom) is not { } room
            || simulation.FindEntity(room.DoorId) is not RoomDoor door)
        {
            WaitOrGoHome(simulation, humanoid);
            return;
        }

        Waiting = -1;
        TargetId = room.Id;
        Phase = PatientPhase.WalkingToRoom;
        humanoid.WalkTo(simulation, RoomVisits.OutsideTile(room, door));
    }

    private void ArriveAtRoom(Simulation simulation, Humanoid humanoid)
    {
        if (simulation.FindEntity(TargetId) is not Room { IsActive: true, DoorQueue: { } queue } room
            || simulation.FindEntity(room.DoorId) is not RoomDoor door)
        {
            Phase = PatientPhase.SeekRoom;
            return;
        }

        if (humanoid.Tile != RoomVisits.OutsideTile(room, door))
        {
            // The walk was cut short; try again.
            Phase = PatientPhase.SeekRoom;
            return;
        }

        Phase = PatientPhase.InDoorQueue;
        queue.Push(simulation, humanoid);
    }

    /// <summary>Through the door: walk to the room's object (GPRoom:commandEnteringPatient).</summary>
    private void EnterRoom(Simulation simulation, Humanoid humanoid)
    {
        if (simulation.FindEntity(TargetId) is not Room room || RoomVisits.PatientObject(simulation, room.Id) is not { } placed)
        {
            LeaveRoom(simulation, humanoid);
            return;
        }

        room.MaxTimes = RoomVisits.GpMaxTimes;
        Phase = PatientPhase.WalkingToObject;
        humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, placed));
    }

    private void LeaveRoom(Simulation simulation, Humanoid humanoid)
    {
        Use = null;
        Phase = PatientPhase.LeavingRoom;
        if (simulation.FindEntity(TargetId) is Room room && simulation.FindEntity(room.DoorId) is RoomDoor door)
        {
            humanoid.WalkTo(simulation, RoomVisits.OutsideTile(room, door));
        }
    }

    /// <summary>
    /// Without a desk or room, the patient stays put and looks again every hour; after
    /// <see cref="WaitingDays"/> days they go home.
    /// </summary>
    private void WaitOrGoHome(Simulation simulation, Humanoid humanoid)
    {
        if (Waiting < 0)
        {
            Waiting = WaitingDays * GameDate.HoursPerDay;
        }

        if (Waiting == 0)
        {
            GoHome(simulation, humanoid);
            return;
        }

        Waiting--;
    }

    /// <summary>Leaves through a random spawn point, as CorsixTH's <c>Humanoid:despawn</c>.</summary>
    private void GoHome(Simulation simulation, Humanoid humanoid)
    {
        Phase = PatientPhase.GoingHome;
        TargetId = 0;
        var points = simulation.Arrivals.SpawnPointsOf(simulation.Map);
        HomeTile = points.Count > 0 ? points[simulation.Random.Next(points.Count)] : humanoid.Tile;
        if (!humanoid.WalkTo(simulation, HomeTile))
        {
            HomeTile = humanoid.Tile;
        }
    }

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteByte((byte)Phase);
        writer.WriteInt32(TargetId);
        writer.WriteTile(QueueTile);
        writer.WriteTile(QueueFace);
        writer.WriteInt32(Waiting);
        writer.WriteTile(HomeTile);
        ObjectUse.WriteOptional(writer, Use);
    }

    internal static PatientBehaviour ReadState(SimReader reader)
    {
        return new PatientBehaviour
        {
            Phase = (PatientPhase)reader.ReadByte(),
            TargetId = reader.ReadInt32(),
            QueueTile = reader.ReadTile(),
            QueueFace = reader.ReadTile(),
            Waiting = reader.ReadInt32(),
            HomeTile = reader.ReadTile(),
            Use = ObjectUse.ReadOptional(reader),
        };
    }
}
