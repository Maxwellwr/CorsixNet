// Ported from CorsixTH CorsixTH/Lua/humanoid_actions/seek_reception.lua, seek_room.lua, queue.lua and
// spawn.lua, CorsixTH/Lua/rooms/gp.lua (commandEnteringPatient) and CorsixTH/Lua/entities/humanoids/patient.lua
// (setDisease, the waiting countdown).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Disasters;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
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

    /// <summary>
    /// Standing at the room's object while the staff uses it with them, such as the nurse handing out a drink at the
    /// pharmacy cabinet.
    /// </summary>
    StandingAtObject,

    /// <summary>In general diagnosis: undressing behind the screen.</summary>
    Undressing,

    /// <summary>In general diagnosis: walking to the crash trolley.</summary>
    WalkingToTrolley,

    /// <summary>In general diagnosis: at the crash trolley, waiting for the doctor to stand ready.</summary>
    AtTrolley,

    /// <summary>In general diagnosis: lying on the crash trolley while the doctor examines them.</summary>
    OnTrolley,

    /// <summary>In general diagnosis: walking back to the screen.</summary>
    WalkingBackToScreen,

    /// <summary>In general diagnosis: dressing behind the screen.</summary>
    Dressing,

    /// <summary>In the toilets: walking to a free toilet.</summary>
    WalkingToLoo,

    /// <summary>In the toilets: on the toilet.</summary>
    UsingLoo,

    /// <summary>In the toilets: walking to a free sink.</summary>
    WalkingToSink,

    /// <summary>In the toilets: washing their hands.</summary>
    UsingSink,

    /// <summary>In the toilets: waiting a moment for a sink to come free.</summary>
    WaitingForSink,

    /// <summary>Walking out of the room's door.</summary>
    LeavingRoom,

    /// <summary>Walking to the edge of the map to leave.</summary>
    GoingHome,

    /// <summary>
    /// Standing about, waiting for a room that is missing or for the player's decision, until they give up and go
    /// home (CorsixTH's <c>waiting</c>).
    /// </summary>
    Waiting,

    /// <summary>Dead: a step aside, then falling down and the trip to heaven (<see cref="DyingStage"/>).</summary>
    Dying,
}

/// <summary>Where a dead patient is on the way out (die.lua's phases).</summary>
public enum DyingStage : byte
{
    /// <summary>Not dying.</summary>
    None,

    /// <summary>The one step aside before falling (Patient:die's <c>MeanderAction():setCount(1)</c>).</summary>
    SteppingAside,

    /// <summary>Falling down (<c>fall_east</c>).</summary>
    Falling,

    /// <summary>The transformation or ending of the fall some patients have (<c>extra_east</c>).</summary>
    Extra,

    /// <summary>Rising from the body as a ghost (<c>rise_east</c>, 11 ticks).</summary>
    Rising,

    /// <summary>Growing wings (<c>wings_east</c>, 11 ticks).</summary>
    Wings,

    /// <summary>Hands together (<c>hands_east</c>, 15 ticks).</summary>
    Hands,

    /// <summary>Flying up, 4 pixels a tick for 30 ticks (<c>fly_east</c>); then the patient is gone.</summary>
    Flying,

    /// <summary>On the way to hell: lying on the ground until the grim reaper is in his place.</summary>
    OnGround,

    /// <summary>A pause of 20 ticks before getting up.</summary>
    AwaitingRise,

    /// <summary>Standing up for the walk to hell (<c>rise_hell_east</c>).</summary>
    RisingToHell,

    /// <summary>Walking to the gates to hell and into them.</summary>
    WalkingToHole,

    /// <summary>Falling into the gates to hell; then the patient is gone.</summary>
    FallingIn,
}

/// <summary>Something a patient does on the spot, interrupting what they were doing.</summary>
public enum PatientSpotAction : byte
{
    None,

    /// <summary>Being sick (<c>VomitAction</c>); puke is left on the floor.</summary>
    Vomit,

    /// <summary>Doing it on the floor (<c>PeeAction</c>); pee is left on the floor.</summary>
    Pee,
}

/// <summary>Where a patient stands with the toilet (CorsixTH's <c>going_to_toilet</c>).</summary>
public enum ToiletVisit : byte
{
    /// <summary><c>"no"</c>.</summary>
    No,

    /// <summary><c>"no-toilets"</c>: the patient wanted the toilet and the hospital has none.</summary>
    NoToilets,

    /// <summary><c>"yes"</c>: on the way to or in the toilets.</summary>
    Yes,
}

/// <summary>What a line of the patient window's treatment history says.</summary>
public enum TreatmentHistoryKind : byte
{
    /// <summary>A room the patient was dealt with in; <see cref="TreatmentHistoryEntry.Id"/> is the room type.</summary>
    Room,

    /// <summary>The patient was diagnosed; <see cref="TreatmentHistoryEntry.Id"/> is the disease.</summary>
    Diagnosis,

    /// <summary>The patient was cured (<c>_S.dynamic_info.patient.actions.cured</c>).</summary>
    Cured,
}

/// <summary>A line of the patient window's treatment history (CorsixTH's <c>treatment_history</c>).</summary>
public readonly record struct TreatmentHistoryEntry(TreatmentHistoryKind Kind, string Id);

/// <summary>What a waiting patient's fax asks the player (CorsixTH's patient faxes).</summary>
public enum PatientQuestion : byte
{
    None,

    /// <summary>No diagnosis room left to go to (<c>makeNoDiagnosisRoomFax</c>): send home, guess the cure or wait.</summary>
    NoDiagnosisRoom,

    /// <summary>A room of the treatment is missing (<c>makeNoTreatmentRoomFax</c>): send home, wait or research.</summary>
    NoTreatmentRoom,
}

/// <summary>The player's answer to a waiting patient's fax (CorsixTH's <c>UIFax:choice</c>).</summary>
public enum PatientAnswer : byte
{
    /// <summary><c>send_home</c>: the patient goes home, not cured.</summary>
    SendHome = 1,

    /// <summary><c>wait</c>: the patient waits another 60 days.</summary>
    Wait = 2,

    /// <summary><c>guess_cure</c>: the disease counts as diagnosed and the patient goes on to treatment; only for a known disease.</summary>
    GuessCure = 3,

    /// <summary><c>research</c>: the patient goes to the research department for an autopsy; only with a researcher there.</summary>
    Research = 4,
}

/// <summary>What kind of room a patient is looking for.</summary>
public enum RoomSeek : byte
{
    /// <summary>The GP's office, after reception.</summary>
    Gp,

    /// <summary>One of the disease's diagnosis rooms, preferably the one the GP chose.</summary>
    Diagnosis,

    /// <summary>The next room of the disease's treatment.</summary>
    Treatment,

    /// <summary>The research department, for an autopsy (the fax's <c>research</c> choice).</summary>
    Research,
}

/// <summary>What a patient does once out of the room they are in.</summary>
internal enum AfterRoom : byte
{
    GoHome,

    /// <summary>Looks for the room in <see cref="PatientBehaviour.Seeking"/>.</summary>
    SeekRoom,

    /// <summary>The treatment failed and the patient dies.</summary>
    Die,

    /// <summary>Out of the toilets: back to the room or the reception desk the patient was going to (<c>ToiletRoom:dealtWithPatient</c>).</summary>
    AfterToilets,

    /// <summary>No toilet was free inside: back into the toilets' queue.</summary>
    ToiletsAgain,
}

/// <summary>
/// A patient's visit: find a reception desk and queue there; when the receptionist has seen them, go to
/// the GP's office, queue at its door and sit down on the chair until the doctor is done. The GP diagnoses
/// the patient's disease (see <see cref="PatientCare"/>), and they go on to a diagnosis or treatment room.
/// In the pharmacy they stand at the cabinet until the nurse has given them their drink, pay and go home cured.
/// </summary>
/// <remarks>
/// Only the GP's office and the pharmacy work so far. A patient sent to any other room finds none and, as in
/// CorsixTH when the room is missing, waits for it or for the player's answer to their fax (<see cref="Question"/>),
/// then goes home; a patient hardly diagnosed goes home at once, one diagnosed far enough has their disease
/// guessed. A waiting patient goes to a room of the kind they wait for as soon as one is built.
/// </remarks>
public sealed class PatientBehaviour : HumanoidBehaviour, IQueueMember
{
    /// <summary>The room type a patient goes to after reception.</summary>
    public const string FirstRoom = "gp";

    /// <summary>How many days a patient waits for a reception desk or the GP's office before going home.</summary>
    private const int WaitingDays = 5;

    private const int MaxDiagnosisRooms = 64;

    /// <summary>What each day costs a patient's health (<c>changeAttribute("health", -0.004)</c>).</summary>
    internal const decimal DailyHealthLoss = 0.004m;

    /// <summary>How many days of health the patient window's graph shows (<c>health_history.size</c>).</summary>
    public const int HealthHistorySize = 20;

    /// <summary>The most lines of treatment history a save may hold.</summary>
    private const int MaxTreatmentHistory = 1024;

    private readonly List<string> _availableDiagnosisRooms = [];
    private readonly List<decimal> _healthHistory = [];
    private readonly List<TreatmentHistoryEntry> _treatmentHistory = [];

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

    /// <summary>The patient's use of the crash trolley with the doctor, while it lasts.</summary>
    public MultiObjectUse? TrolleyUse { get; private set; }

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
    public bool IsStandingInQueue(Humanoid humanoid) => !IsSidetracked && !humanoid.IsMoving && humanoid.Tile == QueueTile;

    /// <summary>Whether the patient sits on the room's object, being seen (CorsixTH's <c>user_of</c>).</summary>
    public bool IsBeingSeen => Phase == PatientPhase.UsingObject && Use is { IsUsing: true };

    /// <summary>Whether the patient stands at the room's object, ready for the staff to use it with them.</summary>
    public bool IsReadyForStaff => Phase == PatientPhase.StandingAtObject && !IsDealtWith;

    /// <summary>How many rooms of the treatment the patient has been through (CorsixTH's <c>cure_rooms_visited</c>).</summary>
    public int CureRoomsVisited { get; internal set; }

    /// <summary>Hours the patient still waits for a missing desk or room (CorsixTH's <c>waiting</c>); −1 when not waiting.</summary>
    private int Waiting { get; set; } = -1;

    /// <summary>Where the patient leaves the map.</summary>
    private TilePosition HomeTile { get; set; }

    /// <summary>
    /// Index into <see cref="AvailableDiagnosisRooms"/> of the room the GP sent the patient to, or −1
    /// (CorsixTH's <c>diagnosis_room</c>).
    /// </summary>
    private int DiagnosisRoomIndex { get; set; } = -1;

    /// <summary>What the patient does after leaving the current room.</summary>
    private AfterRoom Next { get; set; }

    /// <summary>Whether the room's staff is done with the patient (CorsixTH's <c>just_dealt_with</c>).</summary>
    private bool IsDealtWith { get; set; }

    /// <summary>In general diagnosis: whether the doctor has examined the patient on the crash trolley.</summary>
    private bool IsExamined { get; set; }

    /// <summary>How well the patient is, from 0 to 1 (CorsixTH's <c>health</c> attribute).</summary>
    public decimal Health { get; internal set; } = 1m;

    /// <summary>
    /// Health ran out: the patient dies as soon as they are out of rooms and doorways (CorsixTH's <c>set_to_die</c>).
    /// </summary>
    public bool IsSetToDie { get; private set; }

    /// <summary>Which of an emergency's patients this is, from 1, or 0 for none (CorsixTH's <c>is_emergency</c>).</summary>
    public int EmergencyIndex { get; private set; }

    /// <summary>Whether the patient was cured (CorsixTH's <c>cured</c>).</summary>
    public bool IsCured { get; private set; }

    /// <summary>
    /// Tiles to walk before the patient drops litter (CorsixTH's <c>litter_countdown</c>), or −1 until they reach the
    /// hospital's floor; see <see cref="Littering"/>.
    /// </summary>
    public int LitterCountdown { get; internal set; } = -1;

    /// <summary>How thirsty the patient is, from 0 to 1 (CorsixTH's <c>thirst</c> attribute); see <see cref="Needs"/>.</summary>
    public decimal Thirst { get; internal set; }

    /// <summary>How much the patient needs the toilet, from 0 to 1 (CorsixTH's <c>toilet_need</c> attribute).</summary>
    public decimal ToiletNeed { get; internal set; }

    /// <summary>Days before a thirsty patient looks for a drinks machine again (CorsixTH's <c>timeout</c>).</summary>
    public int DrinkTimeout { get; internal set; }

    /// <summary>
    /// The drinks machine the patient is on the way to or buying a can from, or 0. Meanwhile <see cref="Phase"/> keeps
    /// what they were doing, which they go back to with the can (<c>Patient:_constructGetSodaAction</c>).
    /// </summary>
    public int DrinksMachineId { get; private set; }

    /// <summary>The use of the drinks machine once the patient stands at it.</summary>
    private ObjectUse? _drinkUse;

    /// <summary>What the patient does on the spot, interrupting what they were doing, or none.</summary>
    public PatientSpotAction SpotAction { get; private set; }

    /// <summary>Where a dead patient is on the way out; <see cref="DyingStage.None"/> while alive.</summary>
    public DyingStage DyingStage { get; private set; }

    /// <summary>Ticks the current stage of dying has been going for.</summary>
    public int DyingTicks { get; private set; }

    /// <summary>Whether the dead patient fell towards the south (mirrored) rather than the east.</summary>
    private bool _dieSouth;

    /// <summary>Whether the dead patient goes to hell with the grim reaper (die.lua's <c>action_die_tick_reaper</c>).</summary>
    private bool _toHell;

    /// <summary>The grim reaper come for the dead patient, or 0.</summary>
    public int ReaperId { get; private set; }

    /// <summary>Ticks left of the spot action's animation, or −1 until the patient stands still to start it.</summary>
    private int _spotTimer;

    /// <summary>Whether the spot action faces south (drawn mirrored) rather than east.</summary>
    private bool _spotSouth;

    /// <summary>Whether the patient has been sick since they last waited (CorsixTH's <c>has_vomitted</c>).</summary>
    public bool HasVomited { get; internal set; }

    /// <summary>Whether the patient shows the mood of needing the toilet (<c>setMood("poo", "activate")</c>).</summary>
    public bool IsNeedingToilet { get; internal set; }

    /// <summary>Where the patient stands with the toilet (<c>going_to_toilet</c>).</summary>
    public ToiletVisit GoingToToilet { get; internal set; }

    /// <summary>The toilet or sink the patient goes to or uses in the toilets, which nobody else takes (<c>reserved_for</c>).</summary>
    public int ToiletObjectId { get; private set; }

    /// <summary>Rounds of the toilet's in-use animation left (<c>use_time</c>).</summary>
    private int _looRounds;

    /// <summary>Ticks left before looking for a free sink again.</summary>
    private int _sinkWait;

    /// <summary>The room type the patient was going to before the toilets, or empty (<c>next_room_to_visit</c>).</summary>
    private string _afterToiletsRoom = string.Empty;

    /// <summary>Whether the patient was looking for the reception desk before the toilets.</summary>
    private bool _afterToiletsReception;

    /// <summary>
    /// Whether the patient is away from what they were doing: getting a drink or doing something on the spot. Their
    /// <see cref="Phase"/> keeps what they go back to.
    /// </summary>
    public bool IsSidetracked => DrinksMachineId != 0 || SpotAction != PatientSpotAction.None;

    /// <summary>Whether the patient shows the thirsty mood (CorsixTH's <c>setMood("thirsty", "activate")</c>).</summary>
    public bool IsThirsty { get; internal set; }

    /// <summary>Whether the patient shows the sad mood: happiness below 0.3 at the end of the day (<c>sad2</c>).</summary>
    public bool IsSad { get; internal set; }

    /// <summary>
    /// The patient's health on each of their last <see cref="HealthHistorySize"/> days in the hospital, oldest first, for
    /// the patient window's graph (CorsixTH's <c>health_history</c> ring).
    /// </summary>
    public IReadOnlyList<decimal> HealthHistory => _healthHistory;

    /// <summary>
    /// The rooms the patient was dealt with in, their diagnosis and their cure, in order, for the patient window
    /// (CorsixTH's <c>treatment_history</c>).
    /// </summary>
    public IReadOnlyList<TreatmentHistoryEntry> TreatmentHistory => _treatmentHistory;

    /// <summary>Whether the patient has caught an epidemic's disease (CorsixTH's <c>infected</c>); a cure ends it.</summary>
    public bool IsInfected { get; internal set; }

    /// <summary>Whether the patient was vaccinated and passes the disease on no more (CorsixTH's <c>vaccinated</c>).</summary>
    public bool IsVaccinated { get; internal set; }

    /// <summary>Whether an infected patient has tried to infect this one, which happens once (<c>under_infection_attempt</c>).</summary>
    public bool IsUnderInfectionAttempt { get; internal set; }

    /// <summary>Whether the patient got past reception, so an evacuation sends them home (<c>has_passed_reception</c>).</summary>
    public bool HasPassedReception { get; internal set; }

    /// <summary>What the patient's fax asks the player while they wait, or <see cref="PatientQuestion.None"/>.</summary>
    public PatientQuestion Question => Phase == PatientPhase.Waiting ? _question : PatientQuestion.None;

    private PatientQuestion _question;

    /// <summary>Whether the player picked the patient to be vaccinated during a cover-up (<c>marked_for_vaccination</c>).</summary>
    public bool IsMarkedForVaccination { get; internal set; }

    /// <summary>The nurse coming to vaccinate the patient, or 0 (<c>reserved_for</c>).</summary>
    public int VaccinatingNurseId { get; internal set; }

    /// <summary>
    /// Whether the patient stands still, so that a nurse may vaccinate them: in a queue, waiting, or looking for a room
    /// (Epidemic's <c>is_static</c>; benches are not there yet).
    /// </summary>
    internal bool IsStatic(Humanoid humanoid) => Phase switch
    {
        _ when IsSidetracked => false,
        PatientPhase.InReceptionQueue or PatientPhase.InDoorQueue => IsStandingInQueue(humanoid),
        PatientPhase.Waiting or PatientPhase.SeekRoom => !humanoid.IsMoving,
        _ => false,
    };

    internal override void Update(Simulation simulation, Humanoid humanoid)
    {
        if (Phase == PatientPhase.Dying)
        {
            UpdateDying(simulation, humanoid);
            return;
        }

        // Patient:tick: out of rooms and doorways, a patient whose health ran out dies.
        if (IsSetToDie && !IsInRoom && simulation.Map[humanoid.Tile.X, humanoid.Tile.Y].RoomId == 0)
        {
            Die(simulation, humanoid);
            return;
        }

        // CorsixTH counts a patient's wait by the day, whatever they are doing: being sick or getting a drink does not
        // make them wait longer.
        if (IsSidetracked && Phase == PatientPhase.Waiting && Waiting > 0)
        {
            Waiting--;
        }

        if (SpotAction != PatientSpotAction.None)
        {
            DoSpotAction(simulation, humanoid);
            return;
        }

        if (DrinksMachineId != 0)
        {
            GetDrink(simulation, humanoid);
            return;
        }

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
                ArriveAtObject(simulation, humanoid);
                break;
            case PatientPhase.WalkingToLoo:
            case PatientPhase.UsingLoo:
            case PatientPhase.WalkingToSink:
            case PatientPhase.UsingSink:
            case PatientPhase.WaitingForSink:
                ToiletsStep(simulation, humanoid);
                break;
            case PatientPhase.UsingObject:
                if (simulation.FindEntity(TargetId) is Room { RoomType: RoomVisits.WardRoom } ward)
                {
                    WardStep(simulation, humanoid, ward);
                }
                else if (Use is null || Use.Update(simulation, humanoid) == ObjectUseEvent.Done)
                {
                    LeaveRoom(simulation, humanoid);
                }

                break;
            case PatientPhase.StandingAtObject:
                if (IsDealtWith)
                {
                    LeaveRoom(simulation, humanoid);
                }

                break;
            case PatientPhase.AtTrolley:
            case PatientPhase.OnTrolley:
                if (simulation.FindEntity(TargetId) is Room machineRoom && !RoomVisits.UndressesFirst(machineRoom.RoomType))
                {
                    MachineStep(simulation, humanoid, machineRoom);
                }
                else
                {
                    GeneralDiagnosisStep(simulation, humanoid);
                }

                break;
            case PatientPhase.Undressing:
            case PatientPhase.WalkingToTrolley:
            case PatientPhase.WalkingBackToScreen:
            case PatientPhase.Dressing:
                GeneralDiagnosisStep(simulation, humanoid);
                break;
            case PatientPhase.LeavingRoom:
                if (simulation.FindEntity(TargetId) is Room room && room.PatientId == humanoid.Id)
                {
                    room.PatientId = 0;

                    // Room:onHumanoidLeave: there might be other rooms like this one with patients queueing.
                    if (room.DoorQueue is { Count: 0 } && room.IsActive && room.StaffPresent)
                    {
                        RoomVisits.TryToFindNearbyPatients(simulation, room);
                    }
                }

                switch (Next)
                {
                    case AfterRoom.SeekRoom:
                        Phase = PatientPhase.SeekRoom;
                        TargetId = 0;
                        break;
                    case AfterRoom.AfterToilets:
                        AfterToilets(simulation, humanoid);
                        break;
                    case AfterRoom.ToiletsAgain:
                        if (!GoToToilets(simulation, humanoid))
                        {
                            AfterToilets(simulation, humanoid);
                        }

                        break;
                    case AfterRoom.Die:
                        StartDying(simulation, humanoid);
                        break;
                    default:
                        GoHome(simulation, humanoid);
                        break;
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
                    GoHome(simulation, humanoid, ReputationChange.Kicked);
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
    /// still to visit, and who pays; and some thirst and need for the toilet to start with, unless only emergencies
    /// bring the disease.
    /// </summary>
    internal void SetDisease(DiseaseDefinition disease, int insuranceCompany, SimRandom random)
    {
        DiseaseId = disease.Id;
        IsDiagnosed = false;
        DiagnosisProgress = 0;
        _availableDiagnosisRooms.Clear();
        _availableDiagnosisRooms.AddRange(disease.DiagnosisRooms);
        InsuranceCompany = insuranceCompany;

        // Alien patients do not have the animations for these things.
        if (!disease.OnlyEmergency)
        {
            Thirst = Needs.Random(random) * 0.2m;
            ToiletNeed = Needs.Random(random) * 0.2m;
        }
    }

    /// <summary>
    /// <c>Helicopter:spawnPatient</c>: an emergency patient comes diagnosed and heads straight for the last room of the
    /// treatment, unless they find its price too high and go home.
    /// </summary>
    internal void StartEmergency(Simulation simulation, Humanoid humanoid, int index)
    {
        EmergencyIndex = index;
        DiagnosisProgress = 1000;
        var disease = Disease!;
        PatientCare.SetDiagnosed(simulation, humanoid, this, disease);
        CureRoomsVisited = Math.Max(disease.TreatmentRooms.Count - 1, 0);
        if (!PatientCare.AgreesToPay(simulation, humanoid, this, disease))
        {
            GoHome(simulation, humanoid, ReputationChange.OverPriced);
            return;
        }

        Seeking = RoomSeek.Treatment;
        Phase = PatientPhase.SeekRoom;
    }

    /// <summary>
    /// <c>Patient:changeDisease</c>: an undiagnosed patient catches an epidemic's disease instead of their own; the
    /// diagnosis rooms they already went to for the old one are not visited again.
    /// </summary>
    internal void ChangeDisease(DiseaseDefinition disease)
    {
        var old = Disease;
        var available = new List<string>();
        foreach (var room in disease.DiagnosisRooms)
        {
            var visited = old is not null && old.DiagnosisRooms.Contains(room) && !_availableDiagnosisRooms.Contains(room);
            if (!visited)
            {
                available.Add(room);
            }
        }

        _availableDiagnosisRooms.Clear();
        _availableDiagnosisRooms.AddRange(available);
        DiseaseId = disease.Id;
    }

    /// <summary>
    /// <c>Epidemic:evacuateHospital</c>'s <c>goHome("evacuated")</c>: the patient leaves, uncounted, unless already
    /// leaving or dying. One in a room finishes there first (see remarks).
    /// </summary>
    internal void Evacuate(Simulation simulation, Humanoid humanoid)
    {
        if (Phase == PatientPhase.GoingHome || IsSetToDie || Next == AfterRoom.Die)
        {
            return;
        }

        // CorsixTH sends a patient in a room home at once; rooms here cannot be left midway yet.
        if (IsInRoom)
        {
            SetNextGoHome();
            return;
        }

        LeaveQueue(simulation, humanoid);
        Waiting = -1;
        GoHome(simulation, humanoid);
    }

    /// <summary>
    /// <c>UIFax:choice</c> for a patient's fax: sent home (counted as kicked out), waiting another 60 days, or, when the
    /// diagnosis ran out of rooms and the disease is known, the cure guessed and on to treatment if they agree to pay.
    /// </summary>
    internal void Answer(Simulation simulation, Humanoid humanoid, PatientAnswer answer)
    {
        var question = Question;
        if (question == PatientQuestion.None)
        {
            return;
        }

        switch (answer)
        {
            case PatientAnswer.SendHome:
                _question = PatientQuestion.None;
                GoHome(simulation, humanoid, ReputationChange.Kicked);
                break;
            case PatientAnswer.Wait:
                _question = PatientQuestion.None;
                Waiting = PatientCare.WaitingDays * GameDate.HoursPerDay;
                break;
            case PatientAnswer.GuessCure when question == PatientQuestion.NoDiagnosisRoom && Disease is { } disease
                && PatientCare.IsDiscovered(simulation, humanoid, disease):
                _question = PatientQuestion.None;
                PatientCare.SetDiagnosed(simulation, humanoid, this, disease);
                if (PatientCare.AgreesToPay(simulation, humanoid, this, disease))
                {
                    Seeking = RoomSeek.Treatment;
                    SeekTreatmentRoom(simulation, humanoid);
                }
                else
                {
                    GoHome(simulation, humanoid, ReputationChange.OverPriced);
                }

                break;
            case PatientAnswer.Research when question == PatientQuestion.NoTreatmentRoom && CanGoToResearch(simulation, humanoid):
                // setNextAction(SeekRoomAction("research")).
                _question = PatientQuestion.None;
                Waiting = -1;
                Seeking = RoomSeek.Research;
                SeekRoom(simulation, humanoid);
                break;
        }
    }

    /// <summary>
    /// <c>Patient:updateMessage("research")</c>: the fax's research choice is there when the hospital has a research
    /// department and a researcher.
    /// </summary>
    public static bool CanGoToResearch(Simulation simulation, Humanoid humanoid) =>
        simulation.Entities.Any(e => e is Room { IsActive: true, RoomType: RoomVisits.ResearchRoom } room && RoomVisits.Owner(simulation, room) == humanoid.HospitalPlayer)
        && simulation.Entities.Any(e => e is Humanoid { Profile.IsResearcher: true, IsFired: false } staff && staff.Employer == humanoid.HospitalPlayer);

    /// <summary>
    /// <c>UIPatient:goHome</c>: the player sends the patient home, counted as kicked out (<c>goHome("kicked")</c>);
    /// they leave any queue and any question they asked. Not while in a room (see remarks).
    /// </summary>
    internal void SendHome(Simulation simulation, Humanoid humanoid)
    {
        // CorsixTH makes a patient in a room leave it at once; rooms here cannot be left midway yet.
        if (Phase == PatientPhase.GoingHome || IsInRoom || simulation.Map[humanoid.Tile.X, humanoid.Tile.Y].RoomId != 0)
        {
            return;
        }

        LeaveQueue(simulation, humanoid);
        _question = PatientQuestion.None;
        Waiting = -1;
        GoHome(simulation, humanoid, ReputationChange.Kicked);
    }

    /// <summary>
    /// <c>UIPatient:guessDisease</c>: once the diagnosis has begun, and unless the patient is diagnosed, leaving, dying,
    /// in a room, or has a disease the hospital does not know yet, the player guesses: the patient is diagnosed and, if
    /// they agree to pay, leaves what they were doing for the first treatment room; otherwise they go home.
    /// </summary>
    internal void GuessCure(Simulation simulation, Humanoid humanoid)
    {
        if (DiagnosisProgress == 0 || IsDiagnosed || Phase == PatientPhase.GoingHome || IsSetToDie || Next == AfterRoom.Die
            || IsInRoom || simulation.Map[humanoid.Tile.X, humanoid.Tile.Y].RoomId != 0
            || Disease is not { } disease || !PatientCare.IsDiscovered(simulation, humanoid, disease))
        {
            return;
        }

        PatientCare.SetDiagnosed(simulation, humanoid, this, disease);
        LeaveQueue(simulation, humanoid);
        _question = PatientQuestion.None;
        if (PatientCare.AgreesToPay(simulation, humanoid, this, disease))
        {
            // setNextAction(SeekRoomAction(treatment_rooms[1])): the first treatment room.
            Seeking = RoomSeek.Treatment;
            SeekTreatmentRoom(simulation, humanoid);
        }
        else
        {
            GoHome(simulation, humanoid, ReputationChange.OverPriced);
        }
    }

    /// <summary>
    /// Whether the patient is going home or to die, so that another room must not take them from a queue
    /// (<c>tryMovePatient</c> in room.lua; there are no toilets yet to be going to).
    /// </summary>
    internal bool IsGoingHomeOrToDie => Phase is PatientPhase.GoingHome or PatientPhase.Dying || IsSetToDie || Next == AfterRoom.Die;

    /// <summary>
    /// <c>UIQueue:onMouseUp</c>: a patient on their way to a room, or queueing at its door, is dropped on another active
    /// room of the same type: they leave the queue and head for that room instead (<c>setNextAction(room:createEnterAction)</c>).
    /// </summary>
    internal void SendToRoom(Simulation simulation, Humanoid humanoid, Room room)
    {
        if (Phase is not (PatientPhase.WalkingToRoom or PatientPhase.InDoorQueue)
            || simulation.FindEntity(TargetId) is not Room current
            || current.Id == room.Id
            || current.RoomType != room.RoomType
            || !room.IsActive
            || simulation.FindEntity(room.DoorId) is not RoomDoor door)
        {
            return;
        }

        LeaveQueue(simulation, humanoid);
        Waiting = -1;
        TargetId = room.Id;
        Phase = PatientPhase.WalkingToRoom;
        humanoid.WalkTo(simulation, RoomVisits.OutsideTile(room, door));
    }

    /// <summary>
    /// <c>Queue:rerouteAllPatients</c> when a room closes to be edited: a patient walking to it or queueing at its door
    /// heads for another room of the type instead (<c>SeekRoomAction(room_type)</c>), or, with none, looks again as
    /// they would have (a diagnosis room goes back on the list); one going to the toilets goes back to what they were
    /// doing.
    /// </summary>
    internal void LeaveClosedRoom(Simulation simulation, Humanoid humanoid, Room room)
    {
        if (TargetId != room.Id || Phase is not (PatientPhase.WalkingToRoom or PatientPhase.InDoorQueue))
        {
            return;
        }

        LeaveQueue(simulation, humanoid);
        humanoid.StopWalking();
        Waiting = -1;
        TargetId = 0;
        if (string.Equals(room.RoomType, RoomVisits.ToiletsRoom, StringComparison.Ordinal))
        {
            // Back to what they were doing; the need stays, and they look for toilets again later.
            AfterToilets(simulation, humanoid);
            return;
        }

        if (GoToBestRoom(simulation, humanoid, room.RoomType))
        {
            return;
        }

        if (Seeking == RoomSeek.Diagnosis && !_availableDiagnosisRooms.Contains(room.RoomType, StringComparer.Ordinal))
        {
            _availableDiagnosisRooms.Add(room.RoomType);
        }

        Phase = PatientPhase.SeekRoom;
    }

    /// <summary>
    /// <c>Patient:setToDying</c> from <c>Hospital:resolveEmergency</c>: an emergency patient not cured, dead or going home
    /// when time is up will die.
    /// </summary>
    internal void SetToDieUnlessCuredOrLeaving()
    {
        if (!IsCured && Phase != PatientPhase.GoingHome && Next != AfterRoom.Die)
        {
            IsSetToDie = true;
        }
    }

    /// <summary>
    /// The receptionist has seen the patient (<c>ReceptionDesk:tick</c>): on to the GP if they agree to pay for
    /// the consultation, otherwise home.
    /// </summary>
    internal void OnSeenAtReception(Simulation simulation, Humanoid humanoid)
    {
        HasPassedReception = true;
        TargetId = 0;
        Waiting = -1;
        Seeking = RoomSeek.Gp;
        if (Disease is not null && DiseaseCatalog.Find(DiseaseCatalog.GpDiagnosis) is { } consultation
            && !PatientCare.AgreesToPay(simulation, humanoid, this, consultation))
        {
            GoHome(simulation, humanoid, ReputationChange.OverPriced);
            return;
        }

        Phase = PatientPhase.SeekRoom;
    }

    /// <summary>After leaving the room, the patient goes home.</summary>
    internal void SetNextGoHome()
    {
        Next = AfterRoom.GoHome;
    }

    /// <summary>After leaving the room, the patient dies.</summary>
    internal void SetNextDie()
    {
        Next = AfterRoom.Die;
    }

    /// <summary>After leaving a diagnosis room, the patient goes back to the GP.</summary>
    internal void SetNextGp()
    {
        Next = AfterRoom.SeekRoom;
        Seeking = RoomSeek.Gp;
        DiagnosisRoomIndex = -1;
    }

    /// <summary>After leaving the room, the patient looks for the next room of their treatment.</summary>
    internal void SetNextTreatment()
    {
        Next = AfterRoom.SeekRoom;
        Seeking = RoomSeek.Treatment;
        DiagnosisRoomIndex = -1;
    }

    /// <summary>After leaving the room, the patient looks for the diagnosis room the GP chose.</summary>
    internal void SetNextDiagnosis(int index)
    {
        Next = AfterRoom.SeekRoom;
        Seeking = RoomSeek.Diagnosis;
        DiagnosisRoomIndex = index;
    }

    /// <summary>
    /// A room of <paramref name="roomType"/> started working: a patient waiting for such a room goes to look for it, as
    /// CorsixTH's seek room action does when a room is built (<c>action_seek_room_interrupt</c>'s build callback).
    /// </summary>
    internal void OnRoomBuilt(string roomType)
    {
        if (Phase != PatientPhase.Waiting || Disease is not { } disease)
        {
            return;
        }

        var wanted = Seeking switch
        {
            RoomSeek.Treatment => disease.TreatmentRooms.Contains(roomType, StringComparer.Ordinal),
            RoomSeek.Diagnosis => _availableDiagnosisRooms.Contains(roomType, StringComparer.Ordinal),
            RoomSeek.Research => string.Equals(roomType, RoomVisits.ResearchRoom, StringComparison.Ordinal),
            _ => false,
        };
        if (wanted)
        {
            Phase = PatientPhase.SeekRoom;
            Waiting = -1;
        }
    }

    /// <summary>Goes to a place in a queue, then turns to face <paramref name="face"/>.</summary>
    public void TakeQueuePlace(Simulation simulation, Humanoid humanoid, TilePosition tile, TilePosition face)
    {
        QueueFace = face;
        if (QueueTile == tile && (humanoid.Tile == tile || humanoid.Destination == tile))
        {
            return;
        }

        QueueTile = tile;

        // Away at the drinks machine, the patient keeps the place and walks to it with the can (action_queue_get_soda);
        // likewise after being sick or doing it on the floor.
        if (IsSidetracked)
        {
            return;
        }

        if (humanoid.Tile != tile || humanoid.IsMoving)
        {
            humanoid.WalkTo(simulation, tile);
        }
    }

    /// <summary>The room lets the patient in from its queue.</summary>
    internal void LetIn(Simulation simulation, Humanoid humanoid, Room room, RoomDoor door)
    {
        IsDealtWith = false;
        IsExamined = false;
        Next = AfterRoom.GoHome;
        Phase = PatientPhase.EnteringRoom;
        TargetId = room.Id;

        // A patient called in while getting a drink comes in with the can, as CorsixTH's queued actions run in turn.
        if (!IsSidetracked)
        {
            humanoid.WalkTo(simulation, door.InsideTile(room.Area));
        }
    }

    /// <summary>
    /// <c>Patient:_constructGetSodaAction</c>: a thirsty patient in a corridor walks to the drinks machine and buys a can,
    /// then goes back to what they were doing; one in a queue keeps their place (<c>action_queue_get_soda</c>).
    /// </summary>
    internal void GoForDrink(Simulation simulation, Humanoid humanoid, RoomObject machine)
    {
        if (IsSidetracked || Phase is not (PatientPhase.SeekReception or PatientPhase.WalkingToReception or PatientPhase.InReceptionQueue
            or PatientPhase.SeekRoom or PatientPhase.WalkingToRoom or PatientPhase.InDoorQueue or PatientPhase.Waiting))
        {
            return;
        }

        if (humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, machine)))
        {
            DrinksMachineId = machine.Id;
        }
    }

    /// <summary>At the machine, the patient uses it; with the can, they go back to what they were doing.</summary>
    private void GetDrink(Simulation simulation, Humanoid humanoid)
    {
        if (simulation.FindEntity(DrinksMachineId) is not RoomObject machine)
        {
            EndDrink(simulation, humanoid);
            return;
        }

        if (_drinkUse is null)
        {
            if (humanoid.Tile != RoomVisits.UseTile(simulation, machine))
            {
                EndDrink(simulation, humanoid);
                return;
            }

            _drinkUse = ObjectUse.Start(simulation, humanoid, machine);
            return;
        }

        if (_drinkUse.Update(simulation, humanoid) == ObjectUseEvent.Done)
        {
            AfterDrink(simulation, humanoid);
            EndDrink(simulation, humanoid);
        }
    }

    /// <summary>
    /// <c>patient_drinks_machine_after_use</c>: less thirsty, more in need of the toilet, the can is paid for unless the
    /// patient was sent home meanwhile, and six times in ten it will be dropped within 1 to 12 tiles.
    /// </summary>
    private void AfterDrink(Simulation simulation, Humanoid humanoid)
    {
        var random = simulation.Random;
        Thirst = Math.Clamp(Thirst - (0.7m + (Needs.Random(random) * 0.3m)), 0m, 1m);
        ToiletNeed = Math.Clamp(ToiletNeed + 0.05m + (Needs.Random(random) * 0.05m), 0m, 1m);
        IsThirsty = false;
        if (Phase != PatientPhase.GoingHome && humanoid.HospitalPlayer >= 0 && humanoid.HospitalPlayer < simulation.Hospitals.Count)
        {
            // Hospital:sellSodaToPatient and receiveMoneyForProduct.
            var hospital = simulation.Hospitals[humanoid.HospitalPlayer];
            var price = simulation.Rules.SodaPrice;
            simulation.ShowFloatingDollarSign(humanoid, price, insurance: false);
            hospital.Receive(simulation.Hours, price, TransactionKind.Drinks, string.Empty);
            hospital.SodasSold++;
        }

        if (Needs.Random(random) < 0.6m)
        {
            LitterCountdown = random.NextInclusive(1, 12);
        }
    }

    /// <summary>Done with the drinks machine, or it is gone: back to what the patient was doing.</summary>
    private void EndDrink(Simulation simulation, Humanoid humanoid)
    {
        if (_drinkUse is { Phase: not ObjectUsePhase.Done } use)
        {
            humanoid.ClearActionAnimation();
            humanoid.PlaceAt(use.OldTile);
        }

        DrinksMachineId = 0;
        _drinkUse = null;
        CarryOn(simulation, humanoid);
    }

    /// <summary>
    /// <c>SeekToiletsAction</c>: the patient leaves any queue and walks to the best toilets of their hospital
    /// (<c>findRoomNear(self, "toilets", nil, "advanced")</c>), remembering where they were going. Returns false when
    /// the hospital has none or the patient is not in a corridor doing something they can break off.
    /// </summary>
    internal bool GoToToilets(Simulation simulation, Humanoid humanoid)
    {
        if (IsSidetracked || IsSetToDie || Phase is not (PatientPhase.SeekReception or PatientPhase.WalkingToReception or PatientPhase.InReceptionQueue
            or PatientPhase.SeekRoom or PatientPhase.WalkingToRoom or PatientPhase.InDoorQueue or PatientPhase.LeavingRoom)
            || RoomVisits.FindBest(simulation, humanoid, RoomVisits.ToiletsRoom) is not { } room
            || simulation.FindEntity(room.DoorId) is not RoomDoor door)
        {
            return false;
        }

        // Out of the toilets again (no toilet was free), the patient keeps what they were going to do.
        if (Phase != PatientPhase.LeavingRoom)
        {
            _afterToiletsReception = Phase is PatientPhase.SeekReception or PatientPhase.WalkingToReception or PatientPhase.InReceptionQueue;
            _afterToiletsRoom = Phase is PatientPhase.WalkingToRoom or PatientPhase.InDoorQueue && simulation.FindEntity(TargetId) is Room going
                ? going.RoomType
                : string.Empty;
            LeaveQueue(simulation, humanoid);
        }

        GoingToToilet = ToiletVisit.Yes;
        Waiting = -1;
        TargetId = room.Id;
        Phase = PatientPhase.WalkingToRoom;
        humanoid.WalkTo(simulation, RoomVisits.OutsideTile(room, door));
        return true;
    }

    /// <summary>
    /// <c>ToiletRoom:onHumanoidEnter</c>: the patient walks to the nearest free toilet; with none free, they go out and
    /// queue again.
    /// </summary>
    private void EnterToilets(Simulation simulation, Humanoid humanoid, Room room)
    {
        if (RoomVisits.FindFreeObject(simulation, room, humanoid, "loo") is not { } loo)
        {
            Next = AfterRoom.ToiletsAgain;
            LeaveRoom(simulation, humanoid);
            return;
        }

        ToiletObjectId = loo.Id;
        Phase = PatientPhase.WalkingToLoo;
        _looRounds = simulation.Random.NextInclusive(0, 2);
        humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, loo));
    }

    /// <summary>
    /// The toilets' loop callback and the sink: after the rounds on the toilet the patient needs it 0.85 to 1 less;
    /// standard patients then wash their hands at a free sink, or wait a moment for one (half the time) or leave without,
    /// unhappy; then they go back to what they were doing.
    /// </summary>
    private void ToiletsStep(Simulation simulation, Humanoid humanoid)
    {
        if (simulation.FindEntity(TargetId) is not Room room)
        {
            LeaveToilets(simulation, humanoid);
            return;
        }

        var placed = simulation.FindEntity(ToiletObjectId) as RoomObject;
        switch (Phase)
        {
            case PatientPhase.WalkingToLoo:
            case PatientPhase.WalkingToSink:
                if (placed is null || humanoid.Tile != RoomVisits.UseTile(simulation, placed))
                {
                    LeaveToilets(simulation, humanoid);
                    return;
                }

                var onLoo = Phase == PatientPhase.WalkingToLoo;
                Use = ObjectUse.Start(simulation, humanoid, placed, prolonged: onLoo);
                Phase = onLoo ? PatientPhase.UsingLoo : PatientPhase.UsingSink;
                break;
            case PatientPhase.UsingLoo:
                switch (Use?.Update(simulation, humanoid) ?? ObjectUseEvent.Done)
                {
                    case ObjectUseEvent.Loop:
                        _looRounds--;
                        if (_looRounds <= 0)
                        {
                            Relieved(simulation);
                            Use!.Interrupt();
                        }

                        break;
                    case ObjectUseEvent.Done:
                        if (GoingToToilet == ToiletVisit.Yes)
                        {
                            Relieved(simulation);
                        }

                        Use = null;
                        ToiletObjectId = 0;
                        if (humanoid.Type is HumanoidType.StandardMalePatient or HumanoidType.StandardFemalePatient)
                        {
                            GoToSink(simulation, humanoid, room);
                        }
                        else
                        {
                            LeaveToilets(simulation, humanoid);
                        }

                        break;
                }

                break;
            case PatientPhase.UsingSink:
                if (Use is null || Use.Update(simulation, humanoid) == ObjectUseEvent.Done)
                {
                    LeaveToilets(simulation, humanoid);
                }

                break;
            case PatientPhase.WaitingForSink:
                _sinkWait--;
                if (_sinkWait <= 0)
                {
                    GoToSink(simulation, humanoid, room);
                }

                break;
        }
    }

    /// <summary>The toilets' loop callback when the use time runs out.</summary>
    private void Relieved(Simulation simulation)
    {
        IsNeedingToilet = false;
        ToiletNeed = Math.Clamp(ToiletNeed - (0.85m + (Needs.Random(simulation.Random) * 0.15m)), 0m, 1m);
        GoingToToilet = ToiletVisit.No;
    }

    /// <summary><c>toilets_find_sink</c>.</summary>
    private void GoToSink(Simulation simulation, Humanoid humanoid, Room room)
    {
        if (RoomVisits.FindFreeObject(simulation, room, humanoid, "sink") is { } sink)
        {
            ToiletObjectId = sink.Id;
            Phase = PatientPhase.WalkingToSink;
            humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, sink));
            return;
        }

        // If there is a queue to wash hands there is a chance we might not bother, but the patient won't be happy.
        if (simulation.Random.NextInclusive(1, 4) > 2)
        {
            Phase = PatientPhase.WaitingForSink;
            _sinkWait = 5;
        }
        else
        {
            Feelings.ChangeHappiness(simulation, humanoid, -0.08m);
            LeaveToilets(simulation, humanoid);
        }
    }

    /// <summary><c>ToiletRoom:dealtWithPatient</c>: out of the toilets, then back to what the patient was doing.</summary>
    private void LeaveToilets(Simulation simulation, Humanoid humanoid)
    {
        ToiletObjectId = 0;
        Next = AfterRoom.AfterToilets;
        LeaveRoom(simulation, humanoid);
    }

    /// <summary>Back to the room the patient was going to (<c>SeekRoomAction</c>), or to the reception desk.</summary>
    private void AfterToilets(Simulation simulation, Humanoid humanoid)
    {
        TargetId = 0;
        if (GoingToToilet == ToiletVisit.Yes)
        {
            GoingToToilet = ToiletVisit.No;
        }

        var roomType = _afterToiletsRoom;
        _afterToiletsRoom = string.Empty;
        if (_afterToiletsReception)
        {
            _afterToiletsReception = false;
            Phase = PatientPhase.SeekReception;
        }
        else if (roomType.Length == 0 || !GoToBestRoom(simulation, humanoid, roomType))
        {
            Phase = PatientPhase.SeekRoom;
        }
    }

    /// <summary>
    /// <c>Patient:atFullyEmptyTile</c>: walking, standing about, looking for a room or queueing, not on the way home or to
    /// die, on a buildable hospital tile of the patient's own hospital.
    /// </summary>
    internal bool IsAtFullyEmptyTile(Simulation simulation, Humanoid humanoid)
    {
        if (IsSidetracked || IsSetToDie || Phase is not (PatientPhase.SeekReception or PatientPhase.WalkingToReception or PatientPhase.InReceptionQueue
            or PatientPhase.SeekRoom or PatientPhase.WalkingToRoom or PatientPhase.InDoorQueue or PatientPhase.Waiting))
        {
            return false;
        }

        var map = simulation.Map;
        var cell = map[humanoid.Tile.X, humanoid.Tile.Y];
        return cell.Has(TileFlags.Buildable) && cell.Has(TileFlags.Hospital) && cell.Parcel != 0
            && map.GetParcelOwner(cell.Parcel) == humanoid.HospitalPlayer + 1;
    }

    /// <summary>
    /// <c>Patient:vomit</c>: at a fully empty tile and not sick already since they last waited, the patient is sick on the
    /// spot, and unhappy about it.
    /// </summary>
    internal void Vomit(Simulation simulation, Humanoid humanoid)
    {
        if (!IsAtFullyEmptyTile(simulation, humanoid) || HasVomited)
        {
            return;
        }

        StartSpotAction(humanoid, PatientSpotAction.Vomit);
        HasVomited = true;
        Feelings.ChangeHappiness(simulation, humanoid, -0.02m);
    }

    /// <summary><c>Patient:pee</c>: at a fully empty tile the patient does it on the floor, and is sad about it.</summary>
    internal void Pee(Simulation simulation, Humanoid humanoid)
    {
        if (!IsAtFullyEmptyTile(simulation, humanoid))
        {
            return;
        }

        StartSpotAction(humanoid, PatientSpotAction.Pee);
        IsNeedingToilet = false;
        Feelings.ChangeHappiness(simulation, humanoid, -0.02m);
    }

    /// <summary>
    /// <c>interruptAndRequeueAction</c>: the patient stops where they are (finishing a step under way) and does it there.
    /// </summary>
    private void StartSpotAction(Humanoid humanoid, PatientSpotAction action)
    {
        SpotAction = action;
        _spotTimer = -1;
        humanoid.StopWalking();
    }

    /// <summary>
    /// <c>action_vomit_start</c> and <c>action_pee_start</c>: facing east or south at random, the animation plays once; at its
    /// end the puke or pee is left on the tile (<c>World:newLitter</c>) and the patient carries on.
    /// </summary>
    private void DoSpotAction(Simulation simulation, Humanoid humanoid)
    {
        var animations = simulation.Content.FindPatientAnimations(humanoid.Type);
        var animation = SpotAction == PatientSpotAction.Vomit ? animations?.Vomit : animations?.Pee;
        if (_spotTimer < 0)
        {
            _spotSouth = simulation.Random.NextInclusive(0, 1) != 1;
            if (animation is not { } start)
            {
                SpotAction = PatientSpotAction.None;
                CarryOn(simulation, humanoid);
                return;
            }

            humanoid.SetActionAnimation(simulation, start, _spotSouth, humanoid.Tile, humanoid.Tile, 0);
            _spotTimer = start.Length;
            return;
        }

        _spotTimer--;
        if (_spotTimer > 0)
        {
            return;
        }

        humanoid.ClearActionAnimation();
        if (humanoid.HospitalPlayer >= 0)
        {
            Litter.Drop(simulation, humanoid.HospitalPlayer, SpotAction == PatientSpotAction.Vomit ? LitterType.Puke : LitterType.Pee, humanoid.Tile, isMirrored: !_spotSouth);
        }

        SpotAction = PatientSpotAction.None;
        CarryOn(simulation, humanoid);
    }

    /// <summary>
    /// Back to what the patient was doing before getting a drink or doing something on the spot, as CorsixTH's requeued
    /// action: walks start again, a queueing patient walks back to their place.
    /// </summary>
    private void CarryOn(Simulation simulation, Humanoid humanoid)
    {
        switch (Phase)
        {
            case PatientPhase.WalkingToReception:
                Phase = PatientPhase.SeekReception;
                break;
            case PatientPhase.WalkingToRoom:
                if (simulation.FindEntity(TargetId) is Room room && simulation.FindEntity(room.DoorId) is RoomDoor door)
                {
                    humanoid.WalkTo(simulation, RoomVisits.OutsideTile(room, door));
                }
                else
                {
                    Phase = PatientPhase.SeekRoom;
                }

                break;
            case PatientPhase.EnteringRoom:
                if (simulation.FindEntity(TargetId) is Room calledIn && simulation.FindEntity(calledIn.DoorId) is RoomDoor calledDoor)
                {
                    humanoid.WalkTo(simulation, calledDoor.InsideTile(calledIn.Area));
                }

                break;
            case PatientPhase.InReceptionQueue:
            case PatientPhase.InDoorQueue:
                if (humanoid.Tile != QueueTile)
                {
                    humanoid.WalkTo(simulation, QueueTile);
                }

                break;
        }
    }

    /// <summary>
    /// The staff is done with the patient: what comes next is decided, and they get up and leave (CorsixTH's
    /// <c>dealtWithPatient</c>). A second call for the same visit is ignored, as CorsixTH's <c>just_dealt_with</c> does.
    /// </summary>
    internal void DealtWith(Simulation simulation, Room room, Humanoid humanoid)
    {
        if (IsDealtWith)
        {
            return;
        }

        IsDealtWith = true;

        // GPRoom:dealtWithPatient and Room:dealtWithPatient: the room goes into the history before the diagnosis or
        // the cure that it brings. CorsixTH leaves out facilities such as toilets, of which there are none yet.
        AddToTreatmentHistory(new TreatmentHistoryEntry(TreatmentHistoryKind.Room, room.RoomType));
        PatientCare.DealtWith(simulation, room, humanoid, this);
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
            case RoomSeek.Research:
                if (!GoToBestRoom(simulation, humanoid, RoomVisits.ResearchRoom))
                {
                    StartWaiting(simulation, humanoid, PatientQuestion.None);
                }

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
    /// <c>action_seek_room_no_diagnosis_room_found</c> with the hospital's policies: a patient hardly diagnosed
    /// goes home; one fairly far, or with a disease the hospital does not know, waits for the player's decision;
    /// one nearly diagnosed has the disease guessed and goes on to treatment.
    /// </summary>
    private void NoDiagnosisRoomFound(Simulation simulation, Humanoid humanoid)
    {
        var policies = PatientCare.Policies(simulation, humanoid);
        if (Disease is not { } disease || DiagnosisProgress < policies.SendHome)
        {
            GoHome(simulation, humanoid, ReputationChange.Kicked);
        }
        else if (DiagnosisProgress < policies.GuessCure || !PatientCare.IsDiscovered(simulation, humanoid, disease))
        {
            StartWaiting(simulation, humanoid, PatientQuestion.NoDiagnosisRoom);
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
                GoHome(simulation, humanoid, ReputationChange.OverPriced);
            }
        }
    }

    /// <summary>
    /// <c>action_seek_room_find_room</c> for treatment: before the first room every room of the treatment must be
    /// there, later only the next one. If one is missing, the patient waits for it, or for the player to send them home.
    /// </summary>
    private void SeekTreatmentRoom(Simulation simulation, Humanoid humanoid)
    {
        if (Disease is not { } disease || CureRoomsVisited >= disease.TreatmentRooms.Count
            || (CureRoomsVisited == 0 && disease.TreatmentRooms.Any(roomType => FindRoom(simulation, humanoid, roomType) is null))
            || !GoToBestRoom(simulation, humanoid, disease.TreatmentRooms[CureRoomsVisited]))
        {
            // Emergency patients do not ask; they wait for the room to be built.
            StartWaiting(simulation, humanoid, EmergencyIndex > 0 ? PatientQuestion.None : PatientQuestion.NoTreatmentRoom);
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
    private void StartWaiting(Simulation simulation, Humanoid humanoid, PatientQuestion question)
    {
        _question = question;
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

    /// <summary>
    /// Through the door: walk to the room's object, the GP's chair (<c>GPRoom:commandEnteringPatient</c>) or the
    /// pharmacy cabinet's patient side (<c>PharmacyRoom:commandEnteringPatient</c>).
    /// </summary>
    private void EnterRoom(Simulation simulation, Humanoid humanoid)
    {
        if (simulation.FindEntity(TargetId) is Room { RoomType: RoomVisits.ToiletsRoom } toilets)
        {
            EnterToilets(simulation, humanoid, toilets);
            return;
        }

        if (simulation.FindEntity(TargetId) is Room { RoomType: RoomVisits.WardRoom } ward)
        {
            EnterWard(simulation, humanoid, ward);
            return;
        }

        if (simulation.FindEntity(TargetId) is not Room room || RoomVisits.PatientObject(simulation, room) is not { } placed)
        {
            LeaveRoom(simulation, humanoid);
            return;
        }

        Phase = PatientPhase.WalkingToObject;
        if (string.Equals(room.RoomType, RoomVisits.PharmacyRoom, StringComparison.Ordinal))
        {
            humanoid.WalkTo(simulation, RoomVisits.SecondaryUseTile(simulation, placed));
            if (simulation.FindEntity(room.StaffId) is Humanoid { Behaviour: NurseBehaviour nurse } staff)
            {
                nurse.OnPatientEntered(simulation, staff, room);
            }

            return;
        }

        if (string.Equals(room.RoomType, FirstRoom, StringComparison.Ordinal))
        {
            room.MaxTimes = RoomVisits.GpMaxTimes;
        }

        if (string.Equals(room.RoomType, RoomVisits.OperatingTheatreRoom, StringComparison.Ordinal))
        {
            // OperatingTheatreRoom:commandEnteringPatient: the surgeons wash their hands meanwhile.
            foreach (var surgeon in RoomVisits.Surgeons(simulation, room))
            {
                ((DoctorBehaviour)surgeon.Behaviour!).StartWashing(simulation, surgeon, room);
            }
        }

        // The GP's chair, general diagnosis's screen, the psychiatric couch; the blood machine's and jelly moulder's other end.
        humanoid.WalkTo(simulation, RoomVisits.DoctorUsesMachine(room.RoomType)
            ? RoomVisits.SecondaryUseTile(simulation, placed)
            : RoomVisits.UseTile(simulation, placed));
        if (string.Equals(room.RoomType, RoomVisits.PsychRoom, StringComparison.Ordinal)
            && simulation.FindEntity(room.StaffId) is Humanoid { Behaviour: DoctorBehaviour psychiatrist } staffMember)
        {
            // PsychRoom:commandEnteringPatient: the psychiatrist goes to the comfortable chair.
            psychiatrist.StartSession(simulation, staffMember, room);
        }
    }

    /// <summary>
    /// At the object: sit down on the GP's chair, or stand at the pharmacy cabinet facing the nurse's side (east of a
    /// cabinet facing north, otherwise south) until she has given the drink.
    /// </summary>
    private void ArriveAtObject(Simulation simulation, Humanoid humanoid)
    {
        if (simulation.FindEntity(TargetId) is Room { RoomType: RoomVisits.WardRoom }
            && simulation.FindEntity(WardBedId) is RoomObject bed && humanoid.Tile == RoomVisits.UseTile(simulation, bed))
        {
            // The bed's prolonged use with remaining_work of 150 to 600.
            Phase = PatientPhase.UsingObject;
            _wardWork = simulation.Random.NextInclusive(150, 600);
            Use = ObjectUse.Start(simulation, humanoid, bed);
            return;
        }

        if (simulation.FindEntity(TargetId) is not Room room || RoomVisits.PatientObject(simulation, room) is not { } placed)
        {
            LeaveRoom(simulation, humanoid);
            return;
        }

        if (string.Equals(room.RoomType, RoomVisits.PharmacyRoom, StringComparison.Ordinal))
        {
            Phase = PatientPhase.StandingAtObject;
            humanoid.Face(placed.Orientation == Direction.North ? Direction.East : Direction.South);
            return;
        }

        if (string.Equals(room.RoomType, RoomVisits.ScannerRoom, StringComparison.Ordinal)
            && humanoid.Type is not (HumanoidType.StandardMalePatient or HumanoidType.StandardFemalePatient)
            && RoomVisits.ExaminingObject(simulation, room) is { } scanner)
        {
            // ScannerRoom's do_change: only standard patients undress; the others go straight to the scanner.
            Phase = PatientPhase.WalkingToTrolley;
            humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, scanner));
            if (simulation.FindEntity(room.StaffId) is Humanoid { Behaviour: DoctorBehaviour summoned } staff)
            {
                summoned.OnPatientUndressed(simulation, staff, room);
            }

            return;
        }

        if (RoomVisits.UndressesFirst(room.RoomType))
        {
            Phase = PatientPhase.Undressing;
            UseScreen(simulation, humanoid, placed);
            return;
        }

        if (RoomVisits.HasExaminingObject(room.RoomType))
        {
            // UltrascanRoom:commandEnteringPatient: the doctor walks to the machine's other end and waits there.
            Phase = PatientPhase.AtTrolley;
            if (simulation.FindEntity(room.StaffId) is Humanoid { Behaviour: DoctorBehaviour doctor } staff)
            {
                doctor.OnPatientUndressed(simulation, staff, room);
            }
            else if (simulation.FindEntity(room.StaffId) is Humanoid { Behaviour: NurseBehaviour nurse } nurseStaff)
            {
                nurse.OnPatientAtMachine(simulation, nurseStaff, room);
            }

            return;
        }

        Phase = PatientPhase.UsingObject;
        Use = ObjectUse.Start(simulation, humanoid, placed);
    }

    /// <summary>
    /// <c>WardRoom:commandEnteringPatient</c>: the patient takes a free bed and walks to it; without one (the room was
    /// called in error) they leave and queue again.
    /// </summary>
    private void EnterWard(Simulation simulation, Humanoid humanoid, Room ward)
    {
        if (RoomVisits.FindFreeBed(simulation, ward) is not { } bed)
        {
            Next = AfterRoom.SeekRoom;
            LeaveRoom(simulation, humanoid);
            return;
        }

        WardBedId = bed.Id;
        Phase = PatientPhase.WalkingToObject;
        humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, bed));
    }

    /// <summary>
    /// <c>ward_loop_callback2</c> and <c>ward_after_use</c>: each round in bed takes the ward's healing amount off the
    /// work left; when none is left the patient gets up, the room is done with them (diagnosis or treatment) and they leave.
    /// </summary>
    private void WardStep(Simulation simulation, Humanoid humanoid, Room ward)
    {
        switch (Use?.Update(simulation, humanoid) ?? ObjectUseEvent.Done)
        {
            case ObjectUseEvent.Loop:
                _wardWork -= RoomVisits.WardHealingAmount(simulation, ward);
                if (_wardWork <= 0)
                {
                    Use!.Interrupt();
                }

                break;
            case ObjectUseEvent.Done:
                Use = null;
                DealtWith(simulation, ward, humanoid);
                LeaveRoom(simulation, humanoid);
                break;
        }
    }

    /// <summary>In the ward: the bed the patient has taken (<c>reserved_for</c>), or 0.</summary>
    public int WardBedId { get; private set; }

    /// <summary>In the ward: how much of the stay is left (<c>remaining_work</c>).</summary>
    private decimal _wardWork;

    /// <summary>
    /// <c>GeneralDiagRoom:commandEnteringPatient</c>: undress behind the screen; if the doctor is there, lie down on the
    /// crash trolley to be examined (the trolley's multi-use with the doctor); walk back to the screen and dress; then the
    /// room is done with the patient. Without the doctor the patient dresses again, leaves and looks for the room anew.
    /// </summary>
    private void GeneralDiagnosisStep(Simulation simulation, Humanoid humanoid)
    {
        if (simulation.FindEntity(TargetId) is not Room room || RoomVisits.PatientObject(simulation, room) is not { } screen)
        {
            EndScreenUse(humanoid, null);
            LeaveRoom(simulation, humanoid);
            return;
        }

        // The crash trolley, or the cardiogram's machine.
        var trolley = RoomVisits.ExaminingObject(simulation, room);
        switch (Phase)
        {
            case PatientPhase.Undressing:
                if (Use is not null && Use.Update(simulation, humanoid) != ObjectUseEvent.Done)
                {
                    return;
                }

                // action_use_screen_patient_clothes_state: the clothes now hang on the screen.
                Use = null;
                screen.HangingClothes = humanoid.Layers[1];
                if (room.StaffPresent && trolley is not null && string.Equals(room.RoomType, RoomVisits.OperatingTheatreRoom, StringComparison.Ordinal))
                {
                    // In the gown, to the side of the operating table.
                    Phase = PatientPhase.WalkingToTrolley;
                    humanoid.WalkTo(simulation, RoomVisits.SecondaryUseTile(simulation, trolley));
                }
                else if (room.StaffPresent && trolley is not null)
                {
                    Phase = PatientPhase.WalkingToTrolley;
                    humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, trolley));
                    if (simulation.FindEntity(room.StaffId) is Humanoid { Behaviour: DoctorBehaviour summoned } staff)
                    {
                        summoned.OnPatientUndressed(simulation, staff, room);
                    }
                }
                else
                {
                    WalkBackToScreen(simulation, humanoid, screen);
                }

                return;
            case PatientPhase.WalkingToTrolley:
                Phase = PatientPhase.AtTrolley;
                return;
            case PatientPhase.AtTrolley:
                if (trolley is null || !room.StaffPresent)
                {
                    WalkBackToScreen(simulation, humanoid, screen);
                    return;
                }

                if (string.Equals(room.RoomType, RoomVisits.OperatingTheatreRoom, StringComparison.Ordinal))
                {
                    StartOperation(simulation, humanoid, room, trolley);
                    return;
                }

                if (simulation.FindEntity(room.StaffId) is Humanoid { Behaviour: DoctorBehaviour { Phase: DoctorPhase.AtTrolley }, IsMoving: false } doctor)
                {
                    // CardiogramRoom's loop callback runs the in-use animation for five spells of six rounds, the
                    // middle ones slowed to two and three ticks a frame: 54 rounds' time, played here at normal speed.
                    if (string.Equals(room.RoomType, RoomVisits.ScannerRoom, StringComparison.Ordinal))
                    {
                        // ScannerRoom:commandEnteringPatient: both ready, the patient goes into the scanner (its five begin
                        // and five finish animations; it has no in-use one, so its loop callback never runs) while the
                        // doctor works the console.
                        Use = ObjectUse.Start(simulation, humanoid, trolley);
                        Phase = PatientPhase.OnTrolley;
                        ((DoctorBehaviour)doctor.Behaviour!).StartShield(simulation, doctor, room);
                        return;
                    }

                    var rounds = string.Equals(room.RoomType, RoomVisits.CardiogramRoom, StringComparison.Ordinal) ? CardiogramRounds : 0;
                    TrolleyUse = MultiObjectUse.Start(simulation, humanoid, trolley, doctor, inUseRounds: rounds);
                    Phase = PatientPhase.OnTrolley;
                    IsExamined = TrolleyUse is null;
                }

                return;
            case PatientPhase.OnTrolley:
                if (Use is not null)
                {
                    // In the scanner.
                    if (Use.Update(simulation, humanoid) != ObjectUseEvent.Done)
                    {
                        return;
                    }

                    Use = null;
                }
                else if (TrolleyUse is not null)
                {
                    // The operating table's use is the first surgeon's, driven from here.
                    var user = string.Equals(room.RoomType, RoomVisits.OperatingTheatreRoom, StringComparison.Ordinal)
                        && simulation.FindEntity(room.StaffId) is Humanoid surgeon ? surgeon : humanoid;
                    if (TrolleyUse.Update(simulation, user) != ObjectUseEvent.Done)
                    {
                        return;
                    }
                }

                TrolleyUse = null;
                IsExamined = true;
                if (string.Equals(room.RoomType, RoomVisits.OperatingTheatreRoom, StringComparison.Ordinal))
                {
                    foreach (var surgeon in RoomVisits.Surgeons(simulation, room))
                    {
                        ((DoctorBehaviour)surgeon.Behaviour!).OnOperated();
                    }
                }
                if (simulation.FindEntity(room.StaffId) is Humanoid { Behaviour: DoctorBehaviour examiner })
                {
                    examiner.OnExamined(room);
                }

                WalkBackToScreen(simulation, humanoid, screen);
                return;
            case PatientPhase.WalkingBackToScreen:
                Phase = PatientPhase.Dressing;
                UseScreen(simulation, humanoid, screen);
                return;
            case PatientPhase.Dressing:
                if (Use is not null && Use.Update(simulation, humanoid) != ObjectUseEvent.Done)
                {
                    return;
                }

                EndScreenUse(humanoid, screen);
                if (IsExamined)
                {
                    RoomVisits.DealtWithPatient(simulation, room);
                }
                else
                {
                    // Not served: leave and look for such a room again.
                    _availableDiagnosisRooms.Add(room.RoomType);
                    Next = AfterRoom.SeekRoom;
                    Seeking = RoomSeek.Diagnosis;
                    DiagnosisRoomIndex = _availableDiagnosisRooms.Count - 1;
                }

                LeaveRoom(simulation, humanoid);
                return;
        }
    }

    /// <summary>
    /// A room with a machine the patient is examined on with the doctor (<c>UltrascanRoom:commandEnteringPatient</c>):
    /// once the doctor stands at its other end, they use it together (<c>MultiUseObjectAction</c>); then the room is done
    /// with the patient and the doctor meanders again. Without the doctor the patient leaves and looks for the room anew.
    /// </summary>
    private void MachineStep(Simulation simulation, Humanoid humanoid, Room room)
    {
        var machine = RoomVisits.ExaminingObject(simulation, room);
        switch (Phase)
        {
            case PatientPhase.AtTrolley:
                if ((machine is null || !room.StaffPresent) && Seeking is RoomSeek.Treatment or RoomSeek.Research)
                {
                    // The treatment room lost its doctor: leave and look for it again.
                    Next = AfterRoom.SeekRoom;
                    LeaveRoom(simulation, humanoid);
                    return;
                }

                if (machine is null || !room.StaffPresent)
                {
                    _availableDiagnosisRooms.Add(room.RoomType);
                    Next = AfterRoom.SeekRoom;
                    Seeking = RoomSeek.Diagnosis;
                    DiagnosisRoomIndex = _availableDiagnosisRooms.Count - 1;
                    LeaveRoom(simulation, humanoid);
                    return;
                }

                if (string.Equals(room.RoomType, RoomVisits.FractureClinicRoom, StringComparison.Ordinal))
                {
                    // FractureRoom: the patient uses the cast remover with the nurse once she stands at its other side.
                    if (simulation.FindEntity(room.StaffId) is Humanoid { Behaviour: NurseBehaviour { IsAtStation: true }, IsMoving: false } nurse)
                    {
                        TrolleyUse = MultiObjectUse.Start(simulation, humanoid, machine, nurse);
                        Phase = PatientPhase.OnTrolley;
                    }

                    return;
                }

                if (simulation.FindEntity(room.StaffId) is Humanoid { Behaviour: DoctorBehaviour { Phase: DoctorPhase.AtTrolley } doctorBehaviour, IsMoving: false } doctor)
                {
                    if (string.Equals(room.RoomType, RoomVisits.ElectrolysisRoom, StringComparison.Ordinal))
                    {
                        // ElectrolysisRoom:commandEnteringPatient: 1 to 5 electrocutions, times 2 less the doctor's skill;
                        // the patient turns into a standard man, or a woman with a new head and clothes, to show
                        // under the hair; the doctor works the console meanwhile.
                        var skill = (doctor.Profile?.Skill ?? 0) / (decimal)Staff.StaffProfile.MaxSkill;
                        _machineRounds = simulation.Random.NextInclusive(1, 5) * (2 - skill);
                        _electrolysisFatigue = doctor.Fatigue;
                        if (simulation.Random.NextInclusive(0, 1) == 1)
                        {
                            humanoid.Type = HumanoidType.StandardMalePatient;
                        }
                        else
                        {
                            humanoid.Type = HumanoidType.StandardFemalePatient;
                            humanoid.SetLayer(0, (byte)(simulation.Random.NextInclusive(1, 4) * 2));
                            humanoid.SetLayer(1, (byte)(simulation.Random.NextInclusive(0, 3) * 2));
                            humanoid.SetLayer(2, 0);
                        }

                        Use = ObjectUse.Start(simulation, humanoid, machine);
                        doctorBehaviour.StartShield(simulation, doctor, room);
                    }
                    else if (string.Equals(room.RoomType, RoomVisits.DnaFixerRoom, StringComparison.Ordinal))
                    {
                        // DNAFixerRoom:commandEnteringPatient: once both idle, the alien becomes the standard patient they
                        // turn into (setType(change_into), as the fixer's animations are the standard patients'), uses the
                        // fixer once (setProlongedUsage(false)) and the researcher works the console meanwhile.
                        humanoid.Type = humanoid.Type == HumanoidType.AlienFemalePatient ? HumanoidType.StandardFemalePatient : HumanoidType.StandardMalePatient;
                        Use = ObjectUse.Start(simulation, humanoid, machine, prolonged: false);
                        doctorBehaviour.StartShield(simulation, doctor, room);
                    }
                    else if (string.Equals(room.RoomType, RoomVisits.HairRestorationRoom, StringComparison.Ordinal))
                    {
                        // HairRestorationRoom's loop callback ends the use at the first round: sit down, the cap comes down,
                        // a round of restoring, the cap goes up; the doctor works the console meanwhile.
                        _machineRounds = 0;
                        Use = ObjectUse.Start(simulation, humanoid, machine);
                        doctorBehaviour.StartShield(simulation, doctor, room);
                    }
                    else if (string.Equals(room.RoomType, RoomVisits.XRayRoom, StringComparison.Ordinal))
                    {
                        // XRayRoom's loop callback: X-rayed for 2 to 4 rounds, times 2 less the doctor's skill; the doctor
                        // sits at the console meanwhile.
                        var skill = (doctor.Profile?.Skill ?? 0) / (decimal)Staff.StaffProfile.MaxSkill;
                        _machineRounds = simulation.Random.NextInclusive(2, 4) * (2 - skill);
                        Use = ObjectUse.Start(simulation, humanoid, machine);
                        doctorBehaviour.StartShield(simulation, doctor, room);
                    }
                    else if (string.Equals(room.RoomType, RoomVisits.DecontaminationRoom, StringComparison.Ordinal))
                    {
                        // DecontaminationRoom:commandEnteringPatient: length = math.random() * 3 - skill; under 1 the shower
                        // is a short one without loops, else each loop takes one off length - 1 until none is left. The
                        // doctor works the console meanwhile.
                        var skill = (doctor.Profile?.Skill ?? 0) / (decimal)Staff.StaffProfile.MaxSkill;
                        var length = (simulation.Random.Next(1_000_000) / 1_000_000m * 3) - skill;
                        _machineRounds = length - 2;
                        Use = ObjectUse.Start(simulation, humanoid, machine, prolonged: length >= 1);
                        doctorBehaviour.StartShield(simulation, doctor, room);
                    }
                    else if (string.Equals(room.RoomType, RoomVisits.ResearchRoom, StringComparison.Ordinal))
                    {
                        // ResearchRoom:commandEnteringPatient: the researcher uses the autopsy machine with the patient,
                        // who stands by it while its bed comes out (its secondary begin_use is the idle animation).
                        TrolleyUse = MultiObjectUse.Start(simulation, doctor, machine, humanoid, invisiblePhases: (-4, 5));
                    }
                    else if (string.Equals(room.RoomType, RoomVisits.JellyVatRoom, StringComparison.Ordinal))
                    {
                        // JellyVatRoom:commandEnteringPatient: the doctor uses the moulder with the patient, who stays in
                        // view while the doctor presses the button and the door opens, and as the door closes at the end
                        // (setInvisiblePhaseSpan({-3, 4})).
                        TrolleyUse = MultiObjectUse.Start(simulation, doctor, machine, humanoid, invisiblePhases: (-3, 4));
                    }
                    else if (string.Equals(room.RoomType, RoomVisits.BloodMachineRoom, StringComparison.Ordinal))
                    {
                        // BloodMachineRoom:commandEnteringPatient: the doctor uses the machine with the patient, who is in
                        // its animations; its in-use animation plays 3 to 5 times (length 2 to 4, then one more).
                        TrolleyUse = MultiObjectUse.Start(simulation, doctor, machine, humanoid, inUseRounds: simulation.Random.NextInclusive(2, 4));
                    }
                    else
                    {
                        TrolleyUse = MultiObjectUse.Start(simulation, humanoid, machine, doctor);
                    }

                    Phase = PatientPhase.OnTrolley;
                }

                return;
            case PatientPhase.OnTrolley:
                if (Use is not null)
                {
                    var useEvent = Use.Update(simulation, humanoid);
                    if (useEvent == ObjectUseEvent.Loop && string.Equals(room.RoomType, RoomVisits.ElectrolysisRoom, StringComparison.Ordinal))
                    {
                        // electrolysis_loop_callback: once the electrocutions are done, a tired doctor may go on a little:
                        // math.random() <= 0.1 + 2 * (1 - fatigue) ends it.
                        _machineRounds--;
                        if (_machineRounds <= 0 && simulation.Random.Next(1_000_000) / 1_000_000m <= 0.1m + (2 * (1 - _electrolysisFatigue)))
                        {
                            Use.Interrupt();
                        }
                    }
                    else if (useEvent == ObjectUseEvent.Loop)
                    {
                        if (_machineRounds <= 0)
                        {
                            Use.Interrupt();
                        }

                        _machineRounds--;
                    }

                    if (useEvent != ObjectUseEvent.Done)
                    {
                        return;
                    }

                    Use = null;
                }
                else if (TrolleyUse is not null)
                {
                    // The blood machine's and jelly moulder's use is the doctor's, driven from here.
                    var user = RoomVisits.DoctorUsesMachine(room.RoomType)
                        && simulation.FindEntity(room.StaffId) is Humanoid staffMember ? staffMember : humanoid;
                    if (TrolleyUse.Update(simulation, user) != ObjectUseEvent.Done)
                    {
                        return;
                    }
                }

                TrolleyUse = null;
                if (string.Equals(room.RoomType, RoomVisits.ResearchRoom, StringComparison.Ordinal))
                {
                    Autopsied(simulation, humanoid, room);
                    return;
                }

                if (string.Equals(room.RoomType, RoomVisits.FractureClinicRoom, StringComparison.Ordinal))
                {
                    // fracture_clinic_after_use: the casts come off.
                    humanoid.SetLayer(2, 0);
                    humanoid.SetLayer(3, 0);
                    humanoid.SetLayer(4, 0);
                    if (simulation.FindEntity(room.StaffId) is Humanoid { Behaviour: NurseBehaviour nurse })
                    {
                        nurse.OnExamined(room);
                    }
                }

                if (string.Equals(room.RoomType, RoomVisits.HairRestorationRoom, StringComparison.Ordinal))
                {
                    // hair_restoration_after_use: normal hair.
                    humanoid.SetLayer(0, (byte)(humanoid.Layers[0] + 2));
                }

                if (string.Equals(room.RoomType, RoomVisits.SlackTongueRoom, StringComparison.Ordinal))
                {
                    // after_use_slack_tongue: a slack man becomes a standard one; a slack woman's tongue goes from layer 0.
                    if (humanoid.Type == HumanoidType.SlackMalePatient)
                    {
                        humanoid.Type = HumanoidType.StandardMalePatient;
                    }
                    else if (humanoid.Layers[0] >= SlackTongueLayerShift)
                    {
                        humanoid.SetLayer(0, (byte)(humanoid.Layers[0] - SlackTongueLayerShift));
                    }
                }

                if (string.Equals(room.RoomType, RoomVisits.InflationRoom, StringComparison.Ordinal) && humanoid.Layers[0] >= InflatedHeadLayerShift)
                {
                    // inflation_after_use: the head is back to normal size, whether the cure then works or not.
                    humanoid.SetLayer(0, (byte)(humanoid.Layers[0] - InflatedHeadLayerShift));
                }

                if (simulation.FindEntity(room.StaffId) is Humanoid { Behaviour: DoctorBehaviour examiner })
                {
                    examiner.OnExamined(room);
                }

                RoomVisits.DealtWithPatient(simulation, room);
                LeaveRoom(simulation, humanoid);
                return;
        }
    }

    /// <summary>
    /// <c>after_use_autopsy</c>: the patient is dead; the research toward the last room of their treatment gets
    /// <c>AutopsyRschPercent</c> of it, and the autopsy may come out (one in three, two in three when the staff are unhappy),
    /// costing <c>AutopsyRepHitPercent</c> of the hospital's reputation; the researcher goes back to research.
    /// </summary>
    private void Autopsied(Simulation simulation, Humanoid humanoid, Room room)
    {
        if (simulation.FindEntity(room.StaffId) is Humanoid { Behaviour: DoctorBehaviour researcher })
        {
            researcher.OnExamined(room);
        }

        room.PatientId = 0;
        var hospital = simulation.Hospitals[humanoid.HospitalPlayer];
        if (Disease is { TreatmentRooms.Count: > 0 } disease)
        {
            hospital.Research.AddPointsForAutopsy(simulation, hospital, disease.TreatmentRooms[^1]);
        }

        var happiness = EndConditions.AverageHappiness(simulation, hospital, staff: true, minCount: 0);
        var discovered = happiness < 0.25m ? simulation.Random.NextInclusive(1, 3) != 1 : simulation.Random.NextInclusive(1, 3) == 1;
        if (discovered && hospital.Reputation is var reputation)
        {
            hospital.ChangeReputation(simulation.Random, (int)Math.Floor(-reputation * simulation.Rules.Research.AutopsyReputationHitPercent / 100m), null);
        }

        // hosp:removePatient and destroyEntity: gone, without counting as a death.
        TargetId = 0;
        humanoid.ClearActionAnimation();
        humanoid.StopWalking();
        humanoid.IsDespawning = true;
    }

    /// <summary>
    /// <c>operation_standby</c>: once both surgeons stand at the table, the second operates from its far side and the
    /// first uses the table with the patient (operating_table.lua's multi-use), its one-frame in-use animation looping
    /// until the second surgeon has finished (<c>operatring_theatre_after_use</c>).
    /// </summary>
    private void StartOperation(Simulation simulation, Humanoid humanoid, Room room, RoomObject table)
    {
        var surgeons = RoomVisits.Surgeons(simulation, room);
        if (surgeons.Count < 2 || !surgeons.All(s => s is { IsMoving: false, Behaviour: DoctorBehaviour { IsAtTable: true } })
            || simulation.FindEntity(room.StaffId) is not Humanoid first)
        {
            return;
        }

        var second = surgeons.First(s => s.Id != first.Id);
        var length = ((DoctorBehaviour)second.Behaviour!).StartOperating(simulation, second, table);
        var begin = humanoid.Type == HumanoidType.GownedFemalePatient ? 9 + 20 : 9 + 21;
        TrolleyUse = MultiObjectUse.Start(simulation, first, table, humanoid, inUseRounds: Math.Max(0, length - begin - 1));
        Phase = PatientPhase.OnTrolley;
        IsExamined = TrolleyUse is null;
    }

    /// <summary>The cardiogram's in-use rounds after the first (see <see cref="GeneralDiagnosisStep"/>).</summary>
    private const int CardiogramRounds = 53;

    /// <summary>The electrolysis doctor's fatigue as the use began (CorsixTH reads it at each round; it barely changes).</summary>
    private decimal _electrolysisFatigue;

    /// <summary>How much higher a slack woman's layer 0 is than her normal head's (<c>after_use_slack_tongue</c>).</summary>
    private const int SlackTongueLayerShift = 8;

    /// <summary>How much higher a bloated head's layer 0 is than the normal head's (<c>inflation_after_use</c>).</summary>
    private const int InflatedHeadLayerShift = 10;

    /// <summary>Rounds of a machine's in-use animation left (the X-ray's <c>length</c>).</summary>
    private decimal _machineRounds;

    private void WalkBackToScreen(Simulation simulation, Humanoid humanoid, RoomObject screen)
    {
        Phase = PatientPhase.WalkingBackToScreen;
        humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, screen));
    }

    /// <summary>
    /// <c>action_use_screen_start</c>: a dressed patient undresses and becomes a stripped patient, an undressed one dresses
    /// again; the screen's animation shows them meanwhile.
    /// </summary>
    private void UseScreen(Simulation simulation, Humanoid humanoid, RoomObject screen)
    {
        Use = ObjectUse.Start(simulation, humanoid, screen);
        humanoid.Type = humanoid.Type switch
        {
            // The surgeon screen: into a surgical gown.
            HumanoidType.StandardMalePatient when screen.ObjectType == "surgeon_screen" => HumanoidType.GownedMalePatient,
            HumanoidType.StandardFemalePatient when screen.ObjectType == "surgeon_screen" => HumanoidType.GownedFemalePatient,

            // change_to = math.random(1, 3): one of the three kinds of stripped patient.
            HumanoidType.StandardMalePatient => simulation.Random.NextInclusive(1, 3) switch
            {
                1 => HumanoidType.StrippedMalePatient,
                2 => HumanoidType.StrippedMalePatient2,
                _ => HumanoidType.StrippedMalePatient3,
            },
            HumanoidType.StandardFemalePatient => simulation.Random.NextInclusive(1, 3) switch
            {
                1 => HumanoidType.StrippedFemalePatient,
                2 => HumanoidType.StrippedFemalePatient2,
                _ => HumanoidType.StrippedFemalePatient3,
            },
            var other => Dressed(other),
        };
    }

    /// <summary>The patient is dressed again and the screen empty (<c>action_use_screen_normal_state</c>).</summary>
    private void EndScreenUse(Humanoid humanoid, RoomObject? screen)
    {
        Use = null;
        TrolleyUse = null;
        if (screen is not null)
        {
            screen.HangingClothes = null;
        }

        humanoid.Type = Dressed(humanoid.Type);
        humanoid.ClearActionAnimation();
    }

    /// <summary>A stripped patient's type once dressed again; other types as they are.</summary>
    internal static HumanoidType Dressed(HumanoidType type) => type switch
    {
        HumanoidType.StrippedMalePatient or HumanoidType.StrippedMalePatient2 or HumanoidType.StrippedMalePatient3 or HumanoidType.GownedMalePatient => HumanoidType.StandardMalePatient,
        HumanoidType.StrippedFemalePatient or HumanoidType.StrippedFemalePatient2 or HumanoidType.StrippedFemalePatient3 or HumanoidType.GownedFemalePatient => HumanoidType.StandardFemalePatient,
        _ => type,
    };

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
            GoHome(simulation, humanoid, ReputationChange.Kicked);
            return;
        }

        Waiting--;
    }

    /// <summary>Entering, in or leaving a room (CorsixTH's <c>getRoom</c>, <c>is_entering</c> or <c>is_leaving</c>).</summary>
    private bool IsInRoom => Phase is >= PatientPhase.EnteringRoom and <= PatientPhase.LeavingRoom;

    /// <summary><c>Patient:cure</c>: well again, and no longer dying.</summary>
    internal void Cure()
    {
        Health = 1m;
        IsCured = true;
        IsInfected = false;

        // Patient:die does nothing for a cured patient.
        IsSetToDie = false;
    }

    /// <summary>
    /// <c>Patient:_dailyHealthChecks</c>: below 0.01 health goes to 0, and at 0 the patient is set to die. On the way
    /// down, while health is from 0.18 to below 0.22, each day costs a little happiness and one time in 30 the patient is
    /// fed up and goes home, unless they are in the room that cures them.
    /// </summary>
    /// <returns>False when health was already 0, which ends the patient's day.</returns>
    internal void AddToTreatmentHistory(TreatmentHistoryEntry entry) => _treatmentHistory.Add(entry);

    /// <summary><c>Patient:_dailyHealthHistoryRefresh</c>: today's health goes into the graph, the oldest day out.</summary>
    internal void RecordHealth()
    {
        if (_healthHistory.Count == HealthHistorySize)
        {
            _healthHistory.RemoveAt(0);
        }

        _healthHistory.Add(Health);
    }

    internal bool OnEndDayHealth(Simulation simulation, Humanoid humanoid)
    {
        var health = Health;
        if (health < 0.01m)
        {
            if (health == 0)
            {
                IsSetToDie = true;
                return false;
            }

            // Time to say a prayer: dying tomorrow.
            Health = 0;
            return true;
        }

        if (health is >= 0.18m and < 0.22m)
        {
            // Entered the road of deterioration: waiting too long makes sad.
            Feelings.ChangeHappiness(simulation, humanoid, -0.0002m);

            // CorsixTH also lets a patient in another room give up; here only those outside rooms do (see remarks).
            if (!IsInRoom && simulation.Random.NextInclusive(1, 30) == 1)
            {
                LeaveQueue(simulation, humanoid);
                GoHome(simulation, humanoid, ReputationChange.Kicked);
            }
        }

        return true;
    }

    /// <summary><c>Patient:die</c>: the death counts, and the patient is gone.</summary>
    internal void Die(Simulation simulation, Humanoid humanoid, bool animated = true)
    {
        IsSetToDie = false;
        DrinksMachineId = 0;
        _drinkUse = null;
        if (SpotAction != PatientSpotAction.None)
        {
            SpotAction = PatientSpotAction.None;
            humanoid.ClearActionAnimation();
        }

        var hospital = simulation.Hospitals[humanoid.HospitalPlayer];
        PatientCare.RecordDeath(simulation, hospital, Disease is { } disease ? hospital.CaseOf(disease, simulation.Rules.Diseases) : null);
        if (EmergencyIndex > 0)
        {
            Emergencies.PatientDone(simulation, hospital, cured: false);
        }

        LeaveQueue(simulation, humanoid);
        if (animated)
        {
            StartDying(simulation, humanoid);
            return;
        }

        // Room:crashRoom: those killed in an explosion are simply gone.
        TargetId = 0;
        humanoid.StopWalking();
        humanoid.IsDespawning = true;
    }

    /// <summary>
    /// <c>Patient:die</c>'s actions out of a room: one step aside (<c>MeanderAction():setCount(1)</c>) to one of the 24
    /// nearest idle tiles, then the <c>DieAction</c>.
    /// </summary>
    private void StartDying(Simulation simulation, Humanoid humanoid)
    {
        Phase = PatientPhase.Dying;
        TargetId = 0;
        IsSetToDie = false;
        DyingTicks = 0;
        humanoid.StopWalking();
        humanoid.ClearActionAnimation();
        DyingStage = DyingStage.SteppingAside;
        if (simulation.Pathfinder.TryFindIdleTile(simulation.Map, humanoid.Tile, simulation.Random.NextInclusive(1, 24), out var tile)
            && tile != humanoid.Tile)
        {
            humanoid.WalkTo(simulation, tile);
        }
    }

    /// <summary>
    /// die.lua's <c>action_die_start</c> and <c>action_die_tick</c>: the patient falls east or south at random, rises as a
    /// ghost, grows wings, puts the hands together and flies up out of sight.
    /// </summary>
    /// <remarks>
    /// 65 percent of the men, bloaty heads excepted (they have no animations of turning into a standard man and falling into
    /// the hole), go to hell instead (<c>action_die_tick_reaper</c>): they lie as a standard man until the grim reaper has
    /// come, get up, walk into the gates to hell and fall in. Without a place for the hole they go to heaven after all,
    /// from the wings on, as CorsixTH's fallback does.
    /// </remarks>
    private void UpdateDying(Simulation simulation, Humanoid humanoid)
    {
        if (simulation.Content.FindDeathAnimations(humanoid.Type) is not { } animations)
        {
            humanoid.ClearActionAnimation();
            humanoid.IsDespawning = true;
            return;
        }

        if (DyingStage == DyingStage.SteppingAside)
        {
            if (humanoid.IsMoving)
            {
                return;
            }

            _dieSouth = simulation.Random.NextInclusive(0, 1) != 1;

            // So that a bald patient's head does not swell up instead of growing its hair back.
            if (DiseaseId == "baldness")
            {
                humanoid.SetLayer(0, 2);
            }

            _toHell = IsMalePatient(humanoid.Type) && DiseaseId != "bloaty_head" && simulation.Random.NextInclusive(1, 100) <= 65;
            Enter(DyingStage.Falling, animations.Fall);
            return;
        }

        if (UpdateHell(simulation, humanoid))
        {
            return;
        }

        DyingTicks++;
        var length = DyingStage switch
        {
            DyingStage.Falling => animations.FallLength ?? animations.Fall.Length,
            DyingStage.Extra => animations.Extra?.Length ?? 0,
            DyingStage.Rising or DyingStage.Wings => 11,
            DyingStage.Hands => 15,
            _ => 30,
        };
        if (DyingTicks < length)
        {
            return;
        }

        switch (DyingStage)
        {
            case DyingStage.Falling when animations.Extra is { } extra:
                Enter(DyingStage.Extra, extra);
                break;
            case DyingStage.Falling or DyingStage.Extra when _toHell:
                // Phases 1 and 2: lying as a standard man, while the grim reaper and the gates to hell appear.
                humanoid.Type = HumanoidType.StandardMalePatient;
                if (GrimReaperBehaviour.TrySpawn(simulation, humanoid) is { } reaper)
                {
                    ReaperId = reaper.Id;
                    Enter(DyingStage.OnGround, OnGroundMale);
                }
                else
                {
                    _toHell = false;
                    Enter(DyingStage.Wings, simulation.Content.FindDeathAnimations(HumanoidType.StandardMalePatient)?.Wings ?? animations.Wings);
                }

                break;
            case DyingStage.Falling:
            case DyingStage.Extra:
                Enter(DyingStage.Rising, animations.Rise);
                break;
            case DyingStage.Rising:
                // The slack woman's head layer is missing from the wings on: the standard head takes its place.
                if (humanoid.Type == HumanoidType.SlackFemalePatient && humanoid.Layers[0] >= 8)
                {
                    humanoid.SetLayer(0, (byte)(humanoid.Layers[0] - 8));
                }

                Enter(DyingStage.Wings, animations.Wings);
                break;
            case DyingStage.Wings:
                Enter(DyingStage.Hands, animations.Hands);
                break;
            case DyingStage.Hands:
                Enter(DyingStage.Flying, animations.Fly);
                break;
            default:
                humanoid.ClearActionAnimation();
                humanoid.IsDespawning = true;
                break;
        }

        void Enter(DyingStage stage, Definitions.AnimationRef animation)
        {
            DyingStage = stage;
            DyingTicks = 0;
            humanoid.SetActionAnimation(simulation, animation, _dieSouth, humanoid.Tile, humanoid.Tile, 0);
        }
    }

    /// <summary>A standard man lying on the ground (humanoid.lua's <c>on_ground_anim</c>).</summary>
    private static readonly Definitions.AnimationRef OnGroundMale = new(1258, 1);

    /// <summary><c>Humanoid:isMalePatient</c>: a patient whose type is not a woman's.</summary>
    private static bool IsMalePatient(HumanoidType type) => type is HumanoidType.StandardMalePatient or HumanoidType.AlternateMalePatient
        or HumanoidType.SlackMalePatient or HumanoidType.ChewbaccaPatient or HumanoidType.AlienMalePatient or HumanoidType.StrippedMalePatient
        or HumanoidType.StrippedMalePatient2 or HumanoidType.StrippedMalePatient3 or HumanoidType.GownedMalePatient;

    /// <summary>The grim reaper is in his place: after a pause the patient gets up (die.lua's phase 4).</summary>
    internal void ReaperReady(Humanoid humanoid)
    {
        if (DyingStage == DyingStage.OnGround)
        {
            DyingStage = DyingStage.AwaitingRise;
            DyingTicks = 0;
        }
    }

    /// <summary>
    /// die.lua's phases 4 to 6 on the way to hell: a pause, standing up, the walk to the gates to hell and into them, and the
    /// fall. False when the patient is not on the way to hell.
    /// </summary>
    private bool UpdateHell(Simulation simulation, Humanoid humanoid)
    {
        var reaper = simulation.FindEntity(ReaperId) as Humanoid;
        switch (DyingStage)
        {
            case DyingStage.OnGround:
                if (reaper?.Behaviour is not GrimReaperBehaviour)
                {
                    humanoid.ClearActionAnimation();
                    humanoid.IsDespawning = true;
                }

                return true;
            case DyingStage.AwaitingRise:
                if (++DyingTicks >= 20)
                {
                    var riseHell = simulation.Content.FindDeathAnimations(HumanoidType.StandardMalePatient)!.RiseHell;
                    DyingStage = DyingStage.RisingToHell;
                    DyingTicks = 0;
                    humanoid.SetActionAnimation(simulation, riseHell, _dieSouth, humanoid.Tile, humanoid.Tile, 0);
                }

                return true;
            case DyingStage.RisingToHell:
                if (++DyingTicks >= simulation.Content.FindDeathAnimations(HumanoidType.StandardMalePatient)!.RiseHell.Length)
                {
                    humanoid.ClearActionAnimation();
                    DyingStage = DyingStage.WalkingToHole;
                    DyingTicks = 0;
                }

                return true;
            case DyingStage.WalkingToHole:
                if (humanoid.IsMoving)
                {
                    return true;
                }

                if (reaper?.Behaviour is not GrimReaperBehaviour grim)
                {
                    humanoid.IsDespawning = true;
                    return true;
                }

                // To the tile beside the hole, then into it (gates_to_hell's walk_in_to_use).
                var next = humanoid.Tile == grim.PatientUseTile || humanoid.Tile == grim.HoleTile ? grim.HoleTile : grim.PatientUseTile;
                if (humanoid.Tile == grim.HoleTile)
                {
                    DyingStage = DyingStage.FallingIn;
                    DyingTicks = 0;
                    humanoid.SetActionAnimation(simulation, GrimReaperBehaviour.FallingIn, false, grim.HoleTile, grim.HoleTile, 0);
                    grim.PatientFell(simulation, reaper);
                }
                else if (!humanoid.WalkTo(simulation, next))
                {
                    humanoid.RawStepTo(next);
                }

                return true;
            case DyingStage.FallingIn:
                if (++DyingTicks >= GrimReaperBehaviour.FallingIn.Length)
                {
                    humanoid.ClearActionAnimation();
                    humanoid.IsDespawning = true;
                }

                return true;
            default:
                return false;
        }
    }

    /// <summary>Leaves the reception or door queue the patient stands in, if any.</summary>
    private void LeaveQueue(Simulation simulation, Humanoid humanoid) => CurrentQueue(simulation)?.Remove(simulation, humanoid.Id);

    /// <summary>The reception desk's or room door's queue the patient stands in, or null.</summary>
    internal HumanoidQueue? CurrentQueue(Simulation simulation) => Phase switch
    {
        PatientPhase.InReceptionQueue => (simulation.FindEntity(TargetId) as RoomObject)?.Desk?.Queue,
        PatientPhase.InDoorQueue => (simulation.FindEntity(TargetId) as Room)?.DoorQueue,
        _ => null,
    };

    /// <summary>
    /// Leaves through a random spawn point, as CorsixTH's <c>Humanoid:despawn</c>. A patient sent home or put off by the
    /// price counts as not cured (<c>Patient:goHome</c>); the reasons for leaving a room were counted in the room.
    /// </summary>
    private void GoHome(Simulation simulation, Humanoid humanoid, ReputationChange? reason = null)
    {
        if (reason is { } why)
        {
            PatientCare.NotCured(simulation, humanoid, this, why);
        }

        Phase = PatientPhase.GoingHome;
        TargetId = 0;
        var points = simulation.Arrivals.SpawnPointsOf(simulation.Map);
        HomeTile = points.Count > 0 ? points[simulation.Random.Next(points.Count)] : humanoid.Tile;

        // Sent home on the way to the drinks machine, the patient goes; at the machine, they finish buying first (the
        // use is must_happen), and the walk home starts after. So does a patient being sick or doing it on the floor.
        if ((DrinksMachineId != 0 && _drinkUse is not null) || SpotAction != PatientSpotAction.None)
        {
            return;
        }

        DrinksMachineId = 0;
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
        writer.WriteByte((byte)Next);
        writer.WriteBool(IsDealtWith);
        writer.WriteInt32(CureRoomsVisited);
        writer.WriteBool(IsExamined);
        MultiObjectUse.WriteOptional(writer, TrolleyUse);
        writer.WriteDecimal(Health);
        writer.WriteBool(IsSetToDie);
        writer.WriteInt32(EmergencyIndex);
        writer.WriteBool(IsCured);
        writer.WriteBool(IsInfected);
        writer.WriteBool(IsVaccinated);
        writer.WriteBool(IsUnderInfectionAttempt);
        writer.WriteBool(HasPassedReception);
        writer.WriteBool(IsMarkedForVaccination);
        writer.WriteInt32(VaccinatingNurseId);
        writer.WriteByte((byte)_question);
        writer.WriteInt32(LitterCountdown);
        writer.WriteDecimal(Thirst);
        writer.WriteDecimal(ToiletNeed);
        writer.WriteInt32(DrinkTimeout);
        writer.WriteInt32(DrinksMachineId);
        ObjectUse.WriteOptional(writer, _drinkUse);
        writer.WriteByte((byte)SpotAction);
        writer.WriteInt32(_spotTimer);
        writer.WriteBool(_spotSouth);
        writer.WriteByte((byte)DyingStage);
        writer.WriteInt32(DyingTicks);
        writer.WriteBool(_dieSouth);
        writer.WriteBool(_toHell);
        writer.WriteInt32(ReaperId);
        writer.WriteBool(HasVomited);
        writer.WriteBool(IsNeedingToilet);
        writer.WriteByte((byte)GoingToToilet);
        writer.WriteDecimal(_machineRounds);
        writer.WriteInt32(ToiletObjectId);
        writer.WriteInt32(_looRounds);
        writer.WriteInt32(_sinkWait);
        writer.WriteInt32(WardBedId);
        writer.WriteDecimal(_wardWork);
        writer.WriteDecimal(_electrolysisFatigue);
        writer.WriteString(_afterToiletsRoom);
        writer.WriteBool(_afterToiletsReception);
        writer.WriteBool(IsThirsty);
        writer.WriteBool(IsSad);
        writer.WriteInt32(_healthHistory.Count);
        foreach (var health in _healthHistory)
        {
            writer.WriteDecimal(health);
        }

        writer.WriteInt32(_treatmentHistory.Count);
        foreach (var entry in _treatmentHistory)
        {
            writer.WriteByte((byte)entry.Kind);
            writer.WriteString(entry.Id);
        }
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
        patient.Next = (AfterRoom)reader.ReadByte();
        patient.IsDealtWith = reader.ReadBool();
        patient.CureRoomsVisited = reader.ReadInt32();
        patient.IsExamined = reader.ReadBool();
        patient.TrolleyUse = MultiObjectUse.ReadOptional(reader);
        patient.Health = reader.ReadDecimal();
        if (patient.Health is < 0m or > 1m)
        {
            throw new InvalidDataException($"Patient health {patient.Health} is not between 0 and 1.");
        }

        patient.IsSetToDie = reader.ReadBool();
        patient.EmergencyIndex = reader.ReadInt32();
        patient.IsCured = reader.ReadBool();
        patient.IsInfected = reader.ReadBool();
        patient.IsVaccinated = reader.ReadBool();
        patient.IsUnderInfectionAttempt = reader.ReadBool();
        patient.HasPassedReception = reader.ReadBool();
        patient.IsMarkedForVaccination = reader.ReadBool();
        patient.VaccinatingNurseId = reader.ReadInt32();
        var question = reader.ReadByte();
        patient._question = question <= (byte)PatientQuestion.NoTreatmentRoom ? (PatientQuestion)question : throw new InvalidDataException($"Unknown patient question {question}.");
        patient.LitterCountdown = reader.ReadInt32();
        patient.Thirst = reader.ReadDecimal();
        patient.ToiletNeed = reader.ReadDecimal();
        patient.DrinkTimeout = reader.ReadInt32();
        patient.DrinksMachineId = reader.ReadInt32();
        patient._drinkUse = ObjectUse.ReadOptional(reader);
        var spot = reader.ReadByte();
        patient.SpotAction = spot <= (byte)PatientSpotAction.Pee ? (PatientSpotAction)spot : throw new InvalidDataException($"Unknown spot action {spot}.");
        patient._spotTimer = reader.ReadInt32();
        patient._spotSouth = reader.ReadBool();
        if (reader.Version is 0 or >= 74)
        {
            var stage = reader.ReadByte();
            patient.DyingStage = stage <= (byte)DyingStage.FallingIn ? (DyingStage)stage : throw new InvalidDataException($"Unknown dying stage {stage}.");
            patient.DyingTicks = reader.ReadInt32();
            patient._dieSouth = reader.ReadBool();
        }

        if (reader.Version is 0 or >= 75)
        {
            patient._toHell = reader.ReadBool();
            patient.ReaperId = reader.ReadInt32();
        }
        patient.HasVomited = reader.ReadBool();
        patient.IsNeedingToilet = reader.ReadBool();
        var toilet = reader.ReadByte();
        patient.GoingToToilet = toilet <= (byte)ToiletVisit.Yes ? (ToiletVisit)toilet : throw new InvalidDataException($"Unknown toilet visit {toilet}.");
        patient._machineRounds = reader.ReadDecimal();
        patient.ToiletObjectId = reader.ReadInt32();
        patient._looRounds = reader.ReadInt32();
        patient._sinkWait = reader.ReadInt32();
        if (reader.Version is 0 or >= 61)
        {
            patient.WardBedId = reader.ReadInt32();
            patient._wardWork = reader.ReadDecimal();
        }

        if (reader.Version is 0 or >= 67)
        {
            patient._electrolysisFatigue = reader.ReadDecimal();
        }
        patient._afterToiletsRoom = reader.ReadString();
        patient._afterToiletsReception = reader.ReadBool();
        patient.IsThirsty = reader.ReadBool();
        patient.IsSad = reader.ReadBool();
        var days = reader.ReadCount(HealthHistorySize);
        for (var i = 0; i < days; i++)
        {
            patient._healthHistory.Add(reader.ReadDecimal());
        }

        var lines = reader.ReadCount(MaxTreatmentHistory);
        for (var i = 0; i < lines; i++)
        {
            var kind = reader.ReadByte();
            patient._treatmentHistory.Add(new TreatmentHistoryEntry(
                kind <= (byte)TreatmentHistoryKind.Cured ? (TreatmentHistoryKind)kind : throw new InvalidDataException($"Unknown treatment history line {kind}."),
                reader.ReadString()));
        }

        return patient;
    }
}
