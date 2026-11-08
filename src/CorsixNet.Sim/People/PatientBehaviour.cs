// Ported from CorsixTH CorsixTH/Lua/humanoid_actions/seek_reception.lua, seek_room.lua, queue.lua and
// spawn.lua, CorsixTH/Lua/rooms/gp.lua (commandEnteringPatient) and CorsixTH/Lua/entities/humanoids/patient.lua
// (setDisease, the waiting countdown).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Diseases;
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

    /// <summary>Looking for a room of the kind in <see cref="PatientBehaviour.Seeking"/>.</summary>
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

    /// <summary>
    /// Standing about, waiting for a room that is missing or for the player's decision, until they give up and go
    /// home (CorsixTH's <c>waiting</c>).
    /// </summary>
    Waiting,
}

/// <summary>What kind of room a patient is looking for.</summary>
public enum RoomSeek : byte
{
    /// <summary>The GP's office, after reception.</summary>
    Gp,

    /// <summary>One of the disease's diagnosis rooms, preferably the one the GP chose.</summary>
    Diagnosis,

    /// <summary>The first room of the disease's treatment.</summary>
    Treatment,
}

/// <summary>
/// A patient's visit: find a reception desk and queue there; when the receptionist has seen them, go to
/// the GP's office, queue at its door and sit down on the chair until the doctor is done. The GP diagnoses
/// the patient's disease (see <see cref="PatientCare"/>), and they go on to a diagnosis or treatment room.
/// </summary>
/// <remarks>
/// Only the GP's office works so far. A patient sent to any other room finds none and, as in CorsixTH when
/// the room is missing, waits for it or for the player's decision (the faxes are not there yet), then goes
/// home; a patient hardly diagnosed goes home at once, one diagnosed far enough has their disease guessed.
/// </remarks>
public sealed class PatientBehaviour : HumanoidBehaviour
{
    /// <summary>The room type a patient goes to after reception.</summary>
    public const string FirstRoom = "gp";

    /// <summary>How many days a patient waits for a reception desk or the GP's office before going home.</summary>
    private const int WaitingDays = 5;

    private const int MaxDiagnosisRooms = 64;

    private readonly List<string> _availableDiagnosisRooms = [];

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

    /// <summary>The patient's disease, or empty for none.</summary>
    public string DiseaseId { get; private set; } = string.Empty;

    /// <summary>The patient's disease, or null for none.</summary>
    public DiseaseDefinition? Disease => DiseaseId.Length == 0 ? null : DiseaseCatalog.Find(DiseaseId);

    /// <summary>How far the disease is diagnosed, in thousandths (CorsixTH's <c>diagnosis_progress</c>).</summary>
    public int DiagnosisProgress { get; internal set; }

    public bool IsDiagnosed { get; internal set; }

    /// <summary>The disease's diagnosis rooms the patient has not been to yet (CorsixTH's <c>available_diagnosis_rooms</c>).</summary>
    public IReadOnlyList<string> AvailableDiagnosisRooms => _availableDiagnosisRooms;

    /// <summary>The price the patient agreed to pay for the next diagnosis or treatment, or 0.</summary>
    public int PayAmount { get; internal set; }

    /// <summary>The insurance company (1 to 3) that pays for the patient, or 0 when they pay themselves.</summary>
    public int InsuranceCompany { get; private set; }

    /// <summary>What kind of room the patient looks for in <see cref="PatientPhase.SeekRoom"/>.</summary>
    public RoomSeek Seeking { get; private set; }

    /// <summary>Whether the patient stands at their place in the queue, so that the desk may see them.</summary>
    public bool IsStandingInQueue(Humanoid humanoid) => !humanoid.IsMoving && humanoid.Tile == QueueTile;

    /// <summary>Whether the patient sits on the room's object, being seen (CorsixTH's <c>user_of</c>).</summary>
    public bool IsBeingSeen => Phase == PatientPhase.UsingObject && Use is { IsUsing: true };

    /// <summary>Hours the patient still waits for a missing desk or room (CorsixTH's <c>waiting</c>); −1 when not waiting.</summary>
    private int Waiting { get; set; } = -1;

    /// <summary>Where the patient leaves the map.</summary>
    private TilePosition HomeTile { get; set; }

    /// <summary>
    /// Index into <see cref="AvailableDiagnosisRooms"/> of the room the GP sent the patient to, or −1
    /// (CorsixTH's <c>diagnosis_room</c>).
    /// </summary>
    private int DiagnosisRoomIndex { get; set; } = -1;

    /// <summary>Whether the patient goes on to another room after leaving the current one, rather than home.</summary>
    private bool SeekAfterRoom { get; set; }

    /// <summary>Whether the room's staff is done with the patient (CorsixTH's <c>just_dealt_with</c>).</summary>
    private bool IsDealtWith { get; set; }

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

                if (SeekAfterRoom)
                {
                    Phase = PatientPhase.SeekRoom;
                    TargetId = 0;
                }
                else
                {
                    GoHome(simulation, humanoid);
                }

                break;
            case PatientPhase.GoingHome:
                if (humanoid.Tile == HomeTile || !humanoid.WalkTo(simulation, HomeTile))
                {
                    humanoid.IsDespawning = true;
                }

                break;
            case PatientPhase.Waiting:
                if (Waiting <= 0)
                {
                    GoHome(simulation, humanoid);
                }
                else
                {
                    Waiting--;
                }

                break;
        }
    }

    /// <summary>
    /// The first half of <c>Patient:setDisease</c>: the disease, nothing diagnosed yet, all its diagnosis rooms
    /// still to visit, and who pays.
    /// </summary>
    internal void SetDisease(DiseaseDefinition disease, int insuranceCompany)
    {
        DiseaseId = disease.Id;
        IsDiagnosed = false;
        DiagnosisProgress = 0;
        _availableDiagnosisRooms.Clear();
        _availableDiagnosisRooms.AddRange(disease.DiagnosisRooms);
        InsuranceCompany = insuranceCompany;
    }

    /// <summary>
    /// The receptionist has seen the patient (<c>ReceptionDesk:tick</c>): on to the GP if they agree to pay for
    /// the consultation, otherwise home.
    /// </summary>
    internal void OnSeenAtReception(Simulation simulation, Humanoid humanoid)
    {
        TargetId = 0;
        Waiting = -1;
        Seeking = RoomSeek.Gp;
        if (Disease is not null && DiseaseCatalog.Find(DiseaseCatalog.GpDiagnosis) is { } consultation
            && !PatientCare.AgreesToPay(simulation, humanoid, this, consultation))
        {
            GoHome(simulation, humanoid);
            return;
        }

        Phase = PatientPhase.SeekRoom;
    }

    /// <summary>After leaving the room, the patient goes home.</summary>
    internal void SetNextGoHome()
    {
        SeekAfterRoom = false;
    }

    /// <summary>After leaving the room, the patient looks for the first room of their treatment.</summary>
    internal void SetNextTreatment()
    {
        SeekAfterRoom = true;
        Seeking = RoomSeek.Treatment;
        DiagnosisRoomIndex = -1;
    }

    /// <summary>After leaving the room, the patient looks for the diagnosis room the GP chose.</summary>
    internal void SetNextDiagnosis(int index)
    {
        SeekAfterRoom = true;
        Seeking = RoomSeek.Diagnosis;
        DiagnosisRoomIndex = index;
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
        IsDealtWith = false;
        SeekAfterRoom = false;
        Phase = PatientPhase.EnteringRoom;
        TargetId = room.Id;
        humanoid.WalkTo(simulation, door.InsideTile(room.Area));
    }

    /// <summary>
    /// The doctor is done with the patient: what comes next is decided, and they get up and leave (CorsixTH's
    /// <c>dealtWithPatient</c>). A second call for the same visit is ignored, as CorsixTH's <c>just_dealt_with</c> does.
    /// </summary>
    internal void DealtWith(Simulation simulation, Room room, Humanoid humanoid)
    {
        if (IsDealtWith)
        {
            return;
        }

        IsDealtWith = true;
        PatientCare.GpDealtWith(simulation, room, humanoid, this);
        Use?.Interrupt();
    }

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
    /// <c>action_seek_room_start</c>: picks the best room of the kind sought, by distance and how busy it is, and
    /// walks to its door. Without a GP's office the patient waits a while, then goes home.
    /// </summary>
    private void SeekRoom(Simulation simulation, Humanoid humanoid)
    {
        switch (Seeking)
        {
            case RoomSeek.Diagnosis:
                SeekDiagnosisRoom(simulation, humanoid);
                return;
            case RoomSeek.Treatment:
                SeekTreatmentRoom(simulation, humanoid);
                return;
        }

        if (!GoToBestRoom(simulation, humanoid, FirstRoom))
        {
            WaitOrGoHome(simulation, humanoid);
        }
    }

    /// <summary>
    /// <c>action_seek_room_find_room</c> for a diagnosis room: the room the GP chose if there is one, otherwise
    /// any of the disease's remaining diagnosis rooms, tried in random order. The room found is crossed off.
    /// </summary>
    private void SeekDiagnosisRoom(Simulation simulation, Humanoid humanoid)
    {
        var rooms = _availableDiagnosisRooms;
        var found = DiagnosisRoomIndex >= 0 && DiagnosisRoomIndex < rooms.Count && FindRoom(simulation, humanoid, rooms[DiagnosisRoomIndex]) is not null;
        if (!found)
        {
            var toCheck = Enumerable.Range(0, rooms.Count).ToList();
            while (toCheck.Count > 0)
            {
                var at = simulation.Random.Next(toCheck.Count);
                if (FindRoom(simulation, humanoid, rooms[toCheck[at]]) is not null)
                {
                    DiagnosisRoomIndex = toCheck[at];
                    found = true;
                    break;
                }

                toCheck.RemoveAt(at);
            }
        }

        if (found)
        {
            var roomType = rooms[DiagnosisRoomIndex];
            rooms.RemoveAt(DiagnosisRoomIndex);
            DiagnosisRoomIndex = -1;
            GoToBestRoom(simulation, humanoid, roomType);
            return;
        }

        NoDiagnosisRoomFound(simulation, humanoid);
    }

    /// <summary>
    /// <c>action_seek_room_no_diagnosis_room_found</c> with CorsixTH's default policies: a patient hardly diagnosed
    /// goes home; one fairly far, or with a disease the hospital does not know, waits for the player's decision;
    /// one nearly diagnosed has the disease guessed and goes on to treatment.
    /// </summary>
    private void NoDiagnosisRoomFound(Simulation simulation, Humanoid humanoid)
    {
        if (Disease is not { } disease || DiagnosisProgress < PatientCare.SendHome)
        {
            GoHome(simulation, humanoid);
        }
        else if (DiagnosisProgress < PatientCare.GuessCure || !PatientCare.IsDiscovered(simulation, humanoid, disease))
        {
            StartWaiting(simulation, humanoid);
        }
        else
        {
            PatientCare.SetDiagnosed(simulation, humanoid, this, disease);
            if (PatientCare.AgreesToPay(simulation, humanoid, this, disease))
            {
                Seeking = RoomSeek.Treatment;
                SeekTreatmentRoom(simulation, humanoid);
            }
            else
            {
                GoHome(simulation, humanoid);
            }
        }
    }

    /// <summary>
    /// <c>action_seek_room_find_room</c> for treatment: every room of the treatment must be there. If one is
    /// missing, the patient waits for it, or for the player to send them home.
    /// </summary>
    private void SeekTreatmentRoom(Simulation simulation, Humanoid humanoid)
    {
        if (Disease is not { TreatmentRooms: [var first, ..] } disease
            || disease.TreatmentRooms.Any(roomType => FindRoom(simulation, humanoid, roomType) is null)
            || !GoToBestRoom(simulation, humanoid, first))
        {
            StartWaiting(simulation, humanoid);
        }
    }

    /// <summary>The best room of a type that patients can be seen in so far, or null.</summary>
    private static Room? FindRoom(Simulation simulation, Humanoid humanoid, string roomType)
    {
        return RoomVisits.IsSupported(roomType) ? RoomVisits.FindBest(simulation, humanoid, roomType) : null;
    }

    /// <summary>Walks to the door of the best room of the type, if there is one (<c>action_seek_room_goto_room</c>).</summary>
    private bool GoToBestRoom(Simulation simulation, Humanoid humanoid, string roomType)
    {
        if (FindRoom(simulation, humanoid, roomType) is not { } room || simulation.FindEntity(room.DoorId) is not RoomDoor door)
        {
            return false;
        }

        Waiting = -1;
        TargetId = room.Id;
        Phase = PatientPhase.WalkingToRoom;
        humanoid.WalkTo(simulation, RoomVisits.OutsideTile(room, door));
        return true;
    }

    /// <summary>
    /// Waits <see cref="PatientCare.WaitingDays"/> days, then goes home. The patient first steps off to a free spot
    /// nearby, as CorsixTH's patients meander off before they idle.
    /// </summary>
    private void StartWaiting(Simulation simulation, Humanoid humanoid)
    {
        Phase = PatientPhase.Waiting;
        TargetId = 0;
        Waiting = PatientCare.WaitingDays * GameDate.HoursPerDay;
        if (simulation.Pathfinder.TryFindIdleTile(simulation.Map, humanoid.Tile, simulation.Random.Next(8), out var spot) && spot != humanoid.Tile)
        {
            humanoid.WalkTo(simulation, spot);
        }
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
    /// Without a desk or the GP's office, the patient stays put and looks again every hour; after
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
        writer.WriteString(DiseaseId);
        writer.WriteInt32(DiagnosisProgress);
        writer.WriteBool(IsDiagnosed);
        writer.WriteInt32(_availableDiagnosisRooms.Count);
        foreach (var room in _availableDiagnosisRooms)
        {
            writer.WriteString(room);
        }

        writer.WriteInt32(PayAmount);
        writer.WriteInt32(InsuranceCompany);
        writer.WriteByte((byte)Seeking);
        writer.WriteInt32(DiagnosisRoomIndex);
        writer.WriteBool(SeekAfterRoom);
        writer.WriteBool(IsDealtWith);
    }

    internal static PatientBehaviour ReadState(SimReader reader)
    {
        var patient = new PatientBehaviour
        {
            Phase = (PatientPhase)reader.ReadByte(),
            TargetId = reader.ReadInt32(),
            QueueTile = reader.ReadTile(),
            QueueFace = reader.ReadTile(),
            Waiting = reader.ReadInt32(),
            HomeTile = reader.ReadTile(),
            Use = ObjectUse.ReadOptional(reader),
            DiseaseId = reader.ReadString(),
            DiagnosisProgress = reader.ReadInt32(),
            IsDiagnosed = reader.ReadBool(),
        };
        if (patient.DiseaseId.Length != 0 && patient.Disease is null)
        {
            throw new InvalidDataException($"Unknown disease '{patient.DiseaseId}'.");
        }

        var rooms = reader.ReadCount(MaxDiagnosisRooms);
        for (var i = 0; i < rooms; i++)
        {
            patient._availableDiagnosisRooms.Add(reader.ReadString());
        }

        patient.PayAmount = reader.ReadInt32();
        patient.InsuranceCompany = reader.ReadInt32();
        patient.Seeking = (RoomSeek)reader.ReadByte();
        patient.DiagnosisRoomIndex = reader.ReadInt32();
        patient.SeekAfterRoom = reader.ReadBool();
        patient.IsDealtWith = reader.ReadBool();
        return patient;
    }
}
