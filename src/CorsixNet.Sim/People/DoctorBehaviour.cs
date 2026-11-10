// Ported from CorsixTH CorsixTH/Lua/rooms/gp.lua (GPRoom:doStaffUseCycle, commandEnteringStaff) and
// CorsixTH/Lua/rooms/general_diag.lua (GeneralDiagRoom:commandEnteringPatient, the doctor's part) and
// CorsixTH/Lua/rooms/psych.lua (PsychRoom:commandEnteringStaff, commandEnteringPatient) and
// CorsixTH/Lua/rooms/operating_theatre.lua (the surgeons).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Sim.People;

/// <summary>Where a doctor is in their job.</summary>
public enum DoctorPhase : byte
{
    /// <summary>Without a room.</summary>
    Idle,

    WalkingToRoom,
    WalkingToCabinet,
    UsingCabinet,
    WalkingToDesk,
    UsingDesk,

    /// <summary>In general diagnosis: walking to the crash trolley's doctor side.</summary>
    WalkingToTrolley,

    /// <summary>In general diagnosis: standing at the crash trolley for the patient (hidden while examining them).</summary>
    AtTrolley,

    /// <summary>In general diagnosis: meandering about the room until a patient has undressed.</summary>
    Waiting,

    /// <summary>In the X-ray room: at the radiation shield's console while the patient is X-rayed.</summary>
    UsingShield,

    /// <summary>In the research department: walking to a desk, cabinet, computer or atom analyser.</summary>
    WalkingToResearchObject,

    /// <summary>In the research department: working at it.</summary>
    UsingResearchObject,

    /// <summary>In the research department: meandering a few rounds before the next.</summary>
    ResearchMeandering,

    /// <summary>In the psychiatric room: walking to the bookcase, between patients or during a session.</summary>
    WalkingToBookcase,

    /// <summary>In the psychiatric room: reading at the bookcase.</summary>
    UsingBookcase,

    /// <summary>In the psychiatric room: meandering a few rounds between visits to the bookcase.</summary>
    PsychMeandering,

    /// <summary>In the psychiatric room: walking to the comfortable chair for a session.</summary>
    WalkingToChair,

    /// <summary>In the psychiatric room: in the comfortable chair while the patient lies on the couch.</summary>
    UsingChair,

    /// <summary>In the operating theatre: walking to the surgeon screen, and waiting for it to be free.</summary>
    OtWalkingToScreen,

    /// <summary>In the operating theatre: changing into scrubs behind the screen.</summary>
    OtChanging,

    /// <summary>In the operating theatre: in scrubs, meandering, ready for a patient.</summary>
    OtReady,

    /// <summary>In the operating theatre: walking to the sink, and waiting for it to be free.</summary>
    OtWalkingToSink,

    /// <summary>In the operating theatre: washing hands.</summary>
    OtWashing,

    /// <summary>In the operating theatre: walking to their place at the table.</summary>
    OtWalkingToTable,

    /// <summary>In the operating theatre: at the table; the first surgeon operates from here, the patient's use drives it.</summary>
    OtAtTable,

    /// <summary>In the operating theatre: the second surgeon operating from the far side of the table.</summary>
    OtOperating,

    /// <summary>In the training room: a student walking to a free lecture chair.</summary>
    WalkingToLectureChair,

    /// <summary>In the training room: a student on a lecture chair, learning while the room has its consultant.</summary>
    Studying,

    /// <summary>In the training room: the consultant walking to the projector.</summary>
    WalkingToProjector,

    /// <summary>In the training room: the consultant lecturing at the projector.</summary>
    Lecturing,

    /// <summary>In the training room: the consultant walking to a skeleton or bookcase.</summary>
    WalkingToTrainingAid,

    /// <summary>In the training room: the consultant showing the skeleton or reading at the bookcase.</summary>
    UsingTrainingAid,
}

/// <summary>
/// A doctor working in the GP's office: they go back and forth between the filing cabinet and the desk.
/// At the desk, every round of the desk animation counts down; when a patient sits on the chair the doctor
/// may conclude the consultation, the more likely the more skilled they are. In general diagnosis the doctor
/// meanders about the room; once a patient has undressed they go to the crash trolley and wait there, and the
/// patient's use of it shows them both; then they meander again.
/// </summary>
public sealed class DoctorBehaviour : HumanoidBehaviour, IRoomStaff
{
    private const string CabinetType = "cabinet";
    private const string DeskType = "desk";
    private const string Bookcase = "bookcase";
    private const string ComfortableChair = "comfortable_chair";
    private const string SurgeonScreen = "surgeon_screen";
    private const string OperatingSink = "op_sink1";
    private const string OperatingTable = "operating_table";

    public override BehaviourKind Kind => BehaviourKind.Doctor;

    public DoctorPhase Phase { get; private set; }

    /// <summary>The room the doctor works in or is going to, or 0.</summary>
    public int RoomId { get; private set; }

    /// <summary>Rounds of the desk animation left before the doctor goes to the cabinet again.</summary>
    public int DeskUseTime { get; private set; }

    /// <summary>The doctor's use of the cabinet or desk, while it lasts.</summary>
    public ObjectUse? Use { get; private set; }

    /// <summary>Wandering about while without a room, or in general diagnosis between patients.</summary>
    private Meandering Meander { get; init; } = new();

    /// <summary>In the research department: what the researcher goes to or works at, or 0 (<c>reserved_for</c>).</summary>
    public int ResearchObjectId { get; private set; }

    /// <summary>What the researcher worked at last, which they don't take again at once.</summary>
    private int _previousResearchObject;

    /// <summary>Rounds at the desk left (<c>desk_use_time</c>).</summary>
    private int _researchRounds;

    /// <summary>Meander rounds left before the next object (<c>num_meanders</c>).</summary>
    private int _researchMeanders;

    /// <summary>In the psychiatric room: rounds of the session left (<c>duration</c>), or 0 between patients.</summary>
    private int _psychDuration;

    /// <summary>In the operating theatre: the animations still to play (changing at the screen, operating).</summary>
    private readonly List<AnimationRef> _sequence = [];

    /// <summary>Ticks left of the animation playing from <see cref="_sequence"/>.</summary>
    private int _sequenceTimer;

    /// <summary>Where the sequence's animations are drawn from and in which tile's pass.</summary>
    private TilePosition _sequenceTile;

    private TilePosition _sequenceDrawTile;

    /// <summary>The object the sequence's animations show and hide meanwhile.</summary>
    private int _sequenceObject;

    /// <summary>In the training room: the lecture chair, projector, skeleton or bookcase used or gone to, or 0 (<c>reserved_for</c>).</summary>
    public int TrainingObjectId { get; private set; }

    /// <summary>
    /// In the training room: rounds at the projector left before the consultant goes to a skeleton or bookcase
    /// (<c>projector_use_time</c>), or uses of it left.
    /// </summary>
    private int _trainingRounds;

    void IRoomStaff.GoToRoom(Simulation simulation, Humanoid humanoid, Room room, TilePosition inside)
    {
        Meander.Reset();
        RoomId = room.Id;
        Phase = DoctorPhase.WalkingToRoom;
        humanoid.WalkTo(simulation, inside);
    }

    internal override void Update(Simulation simulation, Humanoid humanoid)
    {
        if (humanoid.Type == HumanoidType.Surgeon
            && (Phase is DoctorPhase.Idle or DoctorPhase.WalkingToRoom || simulation.FindEntity(RoomId) is not Room { RoomType: RoomVisits.OperatingTheatreRoom }))
        {
            // The screen's last use, as a surgeon leaves the operating theatre, is left out: they are a doctor again.
            humanoid.Type = HumanoidType.Doctor;
        }

        if (Phase == DoctorPhase.Idle)
        {
            if (!RoomVisits.SettleIdleStaff(simulation, humanoid))
            {
                Meander.Update(simulation, humanoid);
            }

            return;
        }

        if (simulation.FindEntity(RoomId) is not Room room)
        {
            Phase = DoctorPhase.Idle;
            RoomId = 0;
            Use = null;
            Meander.Reset();
            return;
        }

        switch (Phase)
        {
            case DoctorPhase.WalkingToRoom:
                if (!room.Area.Contains(humanoid.Tile))
                {
                    // The walk failed: give the room up, so that it sends for someone else.
                    room.StaffId = 0;
                    RoomId = 0;
                    Phase = DoctorPhase.Idle;
                    return;
                }

                room.StaffPresent = true;

                // Room:commandEnteringStaff.
                RoomVisits.TryToFindNearbyPatients(simulation, room);
                if (string.Equals(room.RoomType, RoomVisits.ResearchRoom, StringComparison.Ordinal))
                {
                    // ResearchRoom:commandEnteringStaff.
                    StartResearchCycle(simulation, humanoid, room);
                }
                else if (string.Equals(room.RoomType, Staff.Training.RoomType, StringComparison.Ordinal))
                {
                    // TrainingRoom:commandEnteringStaff.
                    StartTraining(simulation, humanoid, room);
                }
                else if (string.Equals(room.RoomType, RoomVisits.OperatingTheatreRoom, StringComparison.Ordinal))
                {
                    StartChangingIntoScrubs(simulation, humanoid, room);
                }
                else if (string.Equals(room.RoomType, RoomVisits.PsychRoom, StringComparison.Ordinal))
                {
                    StartPsychCycle(simulation, humanoid, room);
                    if (simulation.FindEntity(room.PatientId) is Humanoid { Behaviour: PatientBehaviour })
                    {
                        // A patient came in before the psychiatrist did.
                        StartSession(simulation, humanoid, room);
                    }
                }
                else if (RoomVisits.HasExaminingObject(room.RoomType))
                {
                    // Room:commandEnteringStaff: the doctor meanders until a patient needs them.
                    Wait();
                }
                else
                {
                    StartCycle(simulation, humanoid, room);
                }

                break;
            case DoctorPhase.WalkingToTrolley:
                Phase = DoctorPhase.AtTrolley;
                break;
            case DoctorPhase.AtTrolley:
                if (!IsPatientComingToTrolley(simulation, room))
                {
                    // The patient left before being examined (sent home, gave up or died): no one to wait for.
                    Wait();
                }

                break;
            case DoctorPhase.Waiting:
                if (IsPatientComingToTrolley(simulation, room))
                {
                    WalkToTrolley(simulation, humanoid, room);
                }
                else
                {
                    Meander.Update(simulation, humanoid);
                }

                break;
            case DoctorPhase.UsingShield:
                if (Use is null || Use.Update(simulation, humanoid) == ObjectUseEvent.Done)
                {
                    Wait();
                }

                break;
            case DoctorPhase.WalkingToResearchObject:
            case DoctorPhase.UsingResearchObject:
            case DoctorPhase.ResearchMeandering:
                ResearchStep(simulation, humanoid, room);
                break;
            case DoctorPhase.WalkingToLectureChair:
            case DoctorPhase.Studying:
            case DoctorPhase.WalkingToProjector:
            case DoctorPhase.Lecturing:
            case DoctorPhase.WalkingToTrainingAid:
            case DoctorPhase.UsingTrainingAid:
                TrainingStep(simulation, humanoid, room);
                break;
            case DoctorPhase.OtWalkingToScreen:
            case DoctorPhase.OtChanging:
            case DoctorPhase.OtReady:
            case DoctorPhase.OtWalkingToSink:
            case DoctorPhase.OtWashing:
            case DoctorPhase.OtWalkingToTable:
            case DoctorPhase.OtOperating:
                OperatingTheatreStep(simulation, humanoid, room);
                break;
            case DoctorPhase.OtAtTable:
                break;
            case DoctorPhase.WalkingToBookcase:
            case DoctorPhase.UsingBookcase:
            case DoctorPhase.PsychMeandering:
            case DoctorPhase.WalkingToChair:
            case DoctorPhase.UsingChair:
                PsychStep(simulation, humanoid, room);
                break;
            case DoctorPhase.WalkingToCabinet:
                if (RoomVisits.FindObject(simulation, room.Id, CabinetType) is { } cabinet)
                {
                    Phase = DoctorPhase.UsingCabinet;
                    Use = ObjectUse.Start(simulation, humanoid, cabinet);
                }
                else
                {
                    WalkToDesk(simulation, humanoid, room);
                }

                break;
            case DoctorPhase.UsingCabinet:
                if (Use is null || Use.Update(simulation, humanoid) == ObjectUseEvent.Done)
                {
                    WalkToDesk(simulation, humanoid, room);
                }

                break;
            case DoctorPhase.WalkingToDesk:
                if (RoomVisits.FindObject(simulation, room.Id, DeskType) is { } desk && humanoid.Profile is { } profile)
                {
                    // desk_use_time = random(floor(3 + 5 * (1 - skill)), ceil(8 + 10 * (1 - skill))).
                    var unskill = StaffProfile.MaxSkill - profile.Skill;
                    DeskUseTime = simulation.Random.NextInclusive(
                        3 + (5 * unskill / StaffProfile.MaxSkill),
                        8 + (((10 * unskill) + StaffProfile.MaxSkill - 1) / StaffProfile.MaxSkill));
                    Phase = DoctorPhase.UsingDesk;
                    Use = ObjectUse.Start(simulation, humanoid, desk);
                }
                else
                {
                    StartCycle(simulation, humanoid, room);
                }

                break;
            case DoctorPhase.UsingDesk:
                switch (Use?.Update(simulation, humanoid) ?? ObjectUseEvent.Done)
                {
                    case ObjectUseEvent.Loop:
                        DeskLoop(simulation, humanoid, room);
                        break;
                    case ObjectUseEvent.Done:
                        StartCycle(simulation, humanoid, room);
                        break;
                }

                break;
        }
    }

    /// <summary>
    /// The GP's loop callback: count down the rounds at the desk, and see whether the patient on the chair
    /// can be dealt with.
    /// </summary>
    private void DeskLoop(Simulation simulation, Humanoid humanoid, Room room)
    {
        var profile = humanoid.Profile!;
        DeskUseTime--;
        if (DeskUseTime == 0)
        {
            // A consultant stays at the desk while not tired (math.random() >= fatigue).
            if (profile.IsConsultant && simulation.Random.Next(1_000_000) >= humanoid.Fatigue * 1_000_000)
            {
                DeskUseTime = simulation.Random.NextInclusive(7, 14);
            }
            else
            {
                Use!.Interrupt();
            }

            if (simulation.FindEntity(room.PatientId) is Humanoid { Behaviour: PatientBehaviour { IsBeingSeen: true } })
            {
                // math.random() <= 0.7 + 0.3 * skill, with skill in thousandths.
                if (simulation.Random.Next(10 * StaffProfile.MaxSkill) < (7 * StaffProfile.MaxSkill) + (3 * profile.Skill) || room.MaxTimes <= 0)
                {
                    RoomVisits.DealtWithPatient(simulation, room);
                }
                else
                {
                    room.MaxTimes--;
                }
            }
        }
    }

    /// <summary>
    /// <c>TrainingRoom:commandEnteringStaff</c>: the consultant, the room's staff member, goes to the projector; a student
    /// to a free lecture chair, to sit on it from then on; without one they leave.
    /// </summary>
    private void StartTraining(Simulation simulation, Humanoid humanoid, Room room)
    {
        Use = null;
        Meander.Reset();
        if (room.StaffId == humanoid.Id)
        {
            StartLectureCycle(simulation, humanoid, room);
            return;
        }

        if (FindFreeTrainingObject(simulation, room, humanoid, "lecture_chair") is { } chair)
        {
            TrainingObjectId = chair.Id;
            Phase = DoctorPhase.WalkingToLectureChair;
            humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, chair));
            return;
        }

        LeaveTrainingRoom(simulation, humanoid);
    }

    /// <summary>Out of the training room, and looking for work like any idle doctor.</summary>
    private void LeaveTrainingRoom(Simulation simulation, Humanoid humanoid)
    {
        Staff.StaffEmployment.Detach(simulation, humanoid);
        Phase = DoctorPhase.Idle;
        RoomId = 0;
        TrainingObjectId = 0;
        Use = null;
        RoomVisits.SettleIdleStaff(simulation, humanoid);
    }

    /// <summary><c>TrainingRoom:doStaffUseCycle</c>: to the projector, to lecture there for 6 to 20 rounds.</summary>
    private void StartLectureCycle(Simulation simulation, Humanoid humanoid, Room room)
    {
        Use = null;
        TrainingObjectId = 0;
        if (RoomVisits.FindObject(simulation, room.Id, "projector") is not { } projector)
        {
            Phase = DoctorPhase.Lecturing;
            return;
        }

        TrainingObjectId = projector.Id;
        _trainingRounds = simulation.Random.NextInclusive(6, 20);
        Phase = DoctorPhase.WalkingToProjector;
        humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, projector));
    }

    /// <summary>
    /// <c>findFreeObjectNearToUse</c> in the training room: the nearest object of the type that no other doctor uses or goes
    /// to.
    /// </summary>
    private static RoomObject? FindFreeTrainingObject(Simulation simulation, Room room, Humanoid humanoid, string type)
    {
        RoomObject? best = null;
        var bestDistance = 0;
        foreach (var placed in simulation.Entities.OfType<RoomObject>())
        {
            if (placed.RoomId != room.Id || placed.IsDestroyed || !string.Equals(placed.ObjectType, type, StringComparison.Ordinal)
                || simulation.Entities.Any(e => e is Humanoid { Behaviour: DoctorBehaviour other } h && h.Id != humanoid.Id && other.TrainingObjectId == placed.Id))
            {
                continue;
            }

            var tile = RoomVisits.UseTile(simulation, placed);
            var distance = Math.Abs(tile.X - humanoid.Tile.X) + Math.Abs(tile.Y - humanoid.Tile.Y);
            if (best is null || distance < bestDistance)
            {
                (best, bestDistance) = (placed, distance);
            }
        }

        return best;
    }

    /// <summary>
    /// The training room's cycles. A student sits on the lecture chair for good. The consultant lectures at the projector;
    /// once its rounds are up, they show a skeleton or read at a bookcase (either at random, when there are both) 3 to 10
    /// times and go back (<c>loop_callback_training</c>); with neither, they lecture on.
    /// </summary>
    private void TrainingStep(Simulation simulation, Humanoid humanoid, Room room)
    {
        var target = simulation.FindEntity(TrainingObjectId) as RoomObject;
        switch (Phase)
        {
            case DoctorPhase.WalkingToLectureChair:
                if (target is null || humanoid.Tile != RoomVisits.UseTile(simulation, target))
                {
                    LeaveTrainingRoom(simulation, humanoid);
                    return;
                }

                Phase = DoctorPhase.Studying;
                Use = ObjectUse.Start(simulation, humanoid, target);
                break;
            case DoctorPhase.Studying:
                if (Use is null || Use.Update(simulation, humanoid) == ObjectUseEvent.Done)
                {
                    // queueAction(MeanderAction()): the lecture chair's use ends only when interrupted.
                    Use = null;
                    Meander.Update(simulation, humanoid);
                }

                break;
            case DoctorPhase.WalkingToProjector:
                if (target is null || humanoid.Tile != RoomVisits.UseTile(simulation, target))
                {
                    StartLectureCycle(simulation, humanoid, room);
                    return;
                }

                Phase = DoctorPhase.Lecturing;
                Use = ObjectUse.Start(simulation, humanoid, target);
                break;
            case DoctorPhase.Lecturing:
                if (Use is null)
                {
                    return;
                }

                switch (Use.Update(simulation, humanoid))
                {
                    case ObjectUseEvent.Loop:
                        _trainingRounds--;
                        if (_trainingRounds == 0 && ChooseTrainingAid(simulation, room, humanoid) is not null)
                        {
                            Use.Interrupt();
                        }

                        _trainingRounds = Math.Max(_trainingRounds, 0);
                        break;
                    case ObjectUseEvent.Done:
                        Use = null;
                        if (ChooseTrainingAid(simulation, room, humanoid) is { } aid)
                        {
                            TrainingObjectId = aid.Id;
                            _trainingRounds = simulation.Random.NextInclusive(3, 10);
                            Phase = DoctorPhase.WalkingToTrainingAid;
                            humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, aid));
                        }
                        else
                        {
                            StartLectureCycle(simulation, humanoid, room);
                        }

                        break;
                }

                break;
            case DoctorPhase.WalkingToTrainingAid:
                if (target is null || humanoid.Tile != RoomVisits.UseTile(simulation, target))
                {
                    StartLectureCycle(simulation, humanoid, room);
                    return;
                }

                Phase = DoctorPhase.UsingTrainingAid;
                Use = ObjectUse.Start(simulation, humanoid, target);
                break;
            case DoctorPhase.UsingTrainingAid:
                if (Use is null || target is null)
                {
                    StartLectureCycle(simulation, humanoid, room);
                    return;
                }

                if (Use.Update(simulation, humanoid) == ObjectUseEvent.Done)
                {
                    _trainingRounds--;
                    Use = _trainingRounds > 0 ? ObjectUse.Start(simulation, humanoid, target) : null;
                    if (Use is null)
                    {
                        StartLectureCycle(simulation, humanoid, room);
                    }
                }

                break;
        }
    }

    /// <summary>A free skeleton or bookcase nearby: the bookcase half the time when there are both.</summary>
    private RoomObject? ChooseTrainingAid(Simulation simulation, Room room, Humanoid humanoid)
    {
        var skeleton = FindFreeTrainingObject(simulation, room, humanoid, "skeleton");
        var bookcase = FindFreeTrainingObject(simulation, room, humanoid, Bookcase);
        if (simulation.Random.Next(2) == 0 && bookcase is not null)
        {
            skeleton = null;
        }

        return skeleton ?? bookcase;
    }

    /// <summary>
    /// <c>ResearchRoom:doStaffUseCycle</c>: to the nearest free desk, cabinet, computer or atom analyser (a desk for 7 to 14
    /// rounds), then 2 to 4 rounds of meandering, then again.
    /// </summary>
    private void StartResearchCycle(Simulation simulation, Humanoid humanoid, Room room)
    {
        Use = null;
        Meander.Reset();
        if (RoomVisits.FindFreeResearchObject(simulation, room, humanoid, _previousResearchObject) is { } placed)
        {
            ResearchObjectId = placed.Id;
            Phase = DoctorPhase.WalkingToResearchObject;
            if (string.Equals(placed.ObjectType, DeskType, StringComparison.Ordinal))
            {
                _researchRounds = simulation.Random.NextInclusive(7, 14);
            }

            humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, placed));
        }
        else
        {
            Phase = DoctorPhase.ResearchMeandering;
        }

        _researchMeanders = simulation.Random.NextInclusive(2, 4);
    }

    /// <summary>
    /// The research cycle: work at the object; the desk gives 100 points once done, a computer 500, an atom analyser 800
    /// (the cabinet none); then meander until the rounds are up.
    /// </summary>
    private void ResearchStep(Simulation simulation, Humanoid humanoid, Room room)
    {
        switch (Phase)
        {
            case DoctorPhase.WalkingToResearchObject:
                if (simulation.FindEntity(ResearchObjectId) is not RoomObject target || humanoid.Tile != RoomVisits.UseTile(simulation, target))
                {
                    ResearchObjectId = 0;
                    Phase = DoctorPhase.ResearchMeandering;
                    return;
                }

                Phase = DoctorPhase.UsingResearchObject;
                Use = ObjectUse.Start(simulation, humanoid, target);
                break;
            case DoctorPhase.UsingResearchObject:
                var used = simulation.FindEntity(ResearchObjectId) as RoomObject;
                switch (Use?.Update(simulation, humanoid) ?? ObjectUseEvent.Done)
                {
                    case ObjectUseEvent.Loop:
                        _researchRounds--;
                        if (_researchRounds <= 0)
                        {
                            Use!.Interrupt();
                        }

                        break;
                    case ObjectUseEvent.Done:
                        var points = used?.ObjectType switch
                        {
                            DeskType => 100,
                            "computer" => 500,
                            "analyser" => 800,
                            _ => 0,
                        };
                        if (points > 0 && humanoid.Employer >= 0 && humanoid.Employer < simulation.Hospitals.Count)
                        {
                            var hospital = simulation.Hospitals[humanoid.Employer];
                            hospital.Research.AddPoints(simulation, hospital, points);
                        }

                        _previousResearchObject = ResearchObjectId;
                        ResearchObjectId = 0;
                        Use = null;
                        Phase = DoctorPhase.ResearchMeandering;
                        Meander.Reset();
                        break;
                }

                break;
            case DoctorPhase.ResearchMeandering:
                Meander.Update(simulation, humanoid, loop: () =>
                {
                    _researchMeanders--;
                    if (_researchMeanders > 0)
                    {
                        return false;
                    }

                    StartResearchCycle(simulation, humanoid, room);
                    return true;
                });
                break;
        }
    }

    /// <summary>
    /// <c>PsychRoom:commandEnteringStaff</c>: to the bookcase and read there, then meander 2 to 8 rounds, then again; without
    /// a bookcase just meander.
    /// </summary>
    private void StartPsychCycle(Simulation simulation, Humanoid humanoid, Room room)
    {
        Use = null;
        _psychDuration = 0;
        Meander.Reset();
        if (RoomVisits.FindObject(simulation, room.Id, Bookcase) is { } bookcase)
        {
            Phase = DoctorPhase.WalkingToBookcase;
            humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, bookcase));
            _researchMeanders = simulation.Random.NextInclusive(2, 8);
        }
        else
        {
            Phase = DoctorPhase.PsychMeandering;
            _researchMeanders = 0;
        }
    }

    /// <summary>
    /// <c>PsychRoom:commandEnteringPatient</c>: the session lasts 16 to 72 rounds of the comfortable chair counted while
    /// the patient lies on the couch; the psychiatrist walks to the chair and sits in it.
    /// </summary>
    internal void StartSession(Simulation simulation, Humanoid humanoid, Room room)
    {
        if (RoomId != room.Id || Phase is DoctorPhase.WalkingToRoom or DoctorPhase.WalkingToChair or DoctorPhase.UsingChair || _psychDuration > 0)
        {
            return;
        }

        _psychDuration = simulation.Random.NextInclusive(16, 72);
        WalkToChair(simulation, humanoid, room);
    }

    private void WalkToChair(Simulation simulation, Humanoid humanoid, Room room)
    {
        Use = null;
        if (RoomVisits.FindObject(simulation, room.Id, ComfortableChair) is not { } chair)
        {
            StartPsychCycle(simulation, humanoid, room);
            return;
        }

        Phase = DoctorPhase.WalkingToChair;
        humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, chair));
    }

    /// <summary>The psychiatrist's day: the bookcase and meandering between patients, the chair and its loop callback during a session.</summary>
    private void PsychStep(Simulation simulation, Humanoid humanoid, Room room)
    {
        switch (Phase)
        {
            case DoctorPhase.WalkingToBookcase:
                if (RoomVisits.FindObject(simulation, room.Id, Bookcase) is { } bookcase && humanoid.Tile == RoomVisits.UseTile(simulation, bookcase))
                {
                    Phase = DoctorPhase.UsingBookcase;
                    Use = ObjectUse.Start(simulation, humanoid, bookcase, prolonged: false);
                }
                else if (_psychDuration > 0)
                {
                    WalkToChair(simulation, humanoid, room);
                }
                else
                {
                    Phase = DoctorPhase.PsychMeandering;
                }

                break;
            case DoctorPhase.UsingBookcase:
                if (Use is null || Use.Update(simulation, humanoid) == ObjectUseEvent.Done)
                {
                    Use = null;
                    if (_psychDuration > 0)
                    {
                        // During a session: back to the chair.
                        WalkToChair(simulation, humanoid, room);
                    }
                    else
                    {
                        Phase = DoctorPhase.PsychMeandering;
                        Meander.Reset();
                    }
                }

                break;
            case DoctorPhase.PsychMeandering:
                Meander.Update(simulation, humanoid, loop: () =>
                {
                    _researchMeanders--;
                    if (_researchMeanders != 0)
                    {
                        return false;
                    }

                    StartPsychCycle(simulation, humanoid, room);
                    return true;
                });
                break;
            case DoctorPhase.WalkingToChair:
                if (RoomVisits.FindObject(simulation, room.Id, ComfortableChair) is { } chair && humanoid.Tile == RoomVisits.UseTile(simulation, chair))
                {
                    Phase = DoctorPhase.UsingChair;
                    Use = ObjectUse.Start(simulation, humanoid, chair);
                }
                else
                {
                    EndSession(simulation, humanoid, room);
                }

                break;
            case DoctorPhase.UsingChair:
                switch (Use?.Update(simulation, humanoid) ?? ObjectUseEvent.Done)
                {
                    case ObjectUseEvent.Loop:
                        SessionLoop(simulation, humanoid, room);
                        break;
                    case ObjectUseEvent.Done:
                        Use = null;
                        if (_psychDuration > 0 && RoomVisits.FindObject(simulation, room.Id, Bookcase) is { } shelf)
                        {
                            // The loop callback sent the psychiatrist to the bookcase.
                            Phase = DoctorPhase.WalkingToBookcase;
                            humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, shelf));
                        }
                        else
                        {
                            StartPsychCycle(simulation, humanoid, room);
                        }

                        break;
                }

                break;
        }
    }

    /// <summary>
    /// <c>psych_loop_callback</c>: a round of the session counts while the patient lies on the couch; at the end the room is
    /// done with the patient and the psychiatrist meanders again. Now and then (every tenth round, one time in two) they
    /// get up to look something up at the bookcase, which shortens the session to at least 8 rounds.
    /// </summary>
    private void SessionLoop(Simulation simulation, Humanoid humanoid, Room room)
    {
        if (simulation.FindEntity(room.PatientId) is not Humanoid { Behaviour: PatientBehaviour patient } || patient.TargetId != room.Id)
        {
            // The patient left the room: the psychiatrist meanders.
            EndSession(simulation, humanoid, room);
            return;
        }

        if (patient is { Phase: PatientPhase.UsingObject, Use.IsUsing: true })
        {
            _psychDuration--;
        }

        if (_psychDuration == 0)
        {
            // King complex patients would change at the screen first; they look like everyone else here.
            EndSession(simulation, humanoid, room);
            RoomVisits.DealtWithPatient(simulation, room);
            return;
        }

        if (_psychDuration % 10 == 0 && simulation.Random.NextInclusive(1, 2) == 1
            && RoomVisits.FindObject(simulation, room.Id, Bookcase) is not null)
        {
            _psychDuration = Math.Max(8, _psychDuration - 72);
            Use!.Interrupt();
        }
    }

    private void EndSession(Simulation simulation, Humanoid humanoid, Room room)
    {
        _psychDuration = 0;
        if (Phase == DoctorPhase.UsingChair && Use is { IsUsing: true })
        {
            // Get up first; the cycle starts once the chair's finish animation is done.
            Use.Interrupt();
            return;
        }

        StartPsychCycle(simulation, humanoid, room);
    }

    /// <summary>
    /// <c>OperatingTheatreRoom:commandEnteringStaff</c>: to the surgeon screen, waiting for it to be free, to change into
    /// scrubs; then meander, ready for a patient.
    /// </summary>
    private void StartChangingIntoScrubs(Simulation simulation, Humanoid humanoid, Room room)
    {
        Use = null;
        _sequence.Clear();
        if (RoomVisits.FindObject(simulation, room.Id, SurgeonScreen) is not { } screen)
        {
            Phase = DoctorPhase.OtReady;
            Meander.Reset();
            return;
        }

        Phase = DoctorPhase.OtWalkingToScreen;
        humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, screen));
    }

    /// <summary><c>commandEnteringPatient</c>: the surgeons wash their hands at the sink, then go to their places at the table.</summary>
    internal void StartWashing(Simulation simulation, Humanoid humanoid, Room room)
    {
        if (RoomId != room.Id || Phase != DoctorPhase.OtReady || RoomVisits.FindObject(simulation, room.Id, OperatingSink) is not { } sink)
        {
            return;
        }

        Phase = DoctorPhase.OtWalkingToSink;
        humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, sink));
    }

    /// <summary>Whether the surgeon stands at their place at the table, ready to operate.</summary>
    public bool IsAtTable => Phase == DoctorPhase.OtAtTable;

    /// <summary>
    /// <c>_buildTableAction2</c>: the second surgeon's part from the far side of the table (operating_table_b.lua): two
    /// begin animations, 2 to 5 rounds of cutting or sewing, two finish animations.
    /// </summary>
    /// <returns>How many ticks it takes, which the first surgeon's use of the table lasts too.</returns>
    internal int StartOperating(Simulation simulation, Humanoid humanoid, RoomObject table)
    {
        _sequence.Clear();
        _sequence.Add(new AnimationRef(2306, 25));
        _sequence.Add(new AnimationRef(2330, 5));
        var loops = simulation.Random.NextInclusive(2, 5);
        for (var i = 0; i < loops; i++)
        {
            _sequence.Add(simulation.Random.Next(2) == 0 ? new AnimationRef(4890, 34) : new AnimationRef(2326, 9));
        }

        _sequence.Add(new AnimationRef(2334, 5));
        _sequence.Add(new AnimationRef(2342, 12));
        _sequenceTile = _sequenceDrawTile = RoomVisits.OperatingTableSlaveTile(simulation, table);
        _sequenceObject = table.Id;
        Phase = DoctorPhase.OtOperating;
        PlayNext(simulation, humanoid);
        return _sequence.Sum(animation => animation.Length);
    }

    /// <summary>The operation is over (<c>operatring_theatre_table_after_use</c>): the surgeon meanders, ready again.</summary>
    internal void OnOperated()
    {
        if (Phase is DoctorPhase.OtAtTable or DoctorPhase.OtOperating)
        {
            Phase = DoctorPhase.OtReady;
            _sequence.Clear();
            Meander.Reset();
        }
    }

    /// <summary>The surgeon's part of the operating theatre.</summary>
    private void OperatingTheatreStep(Simulation simulation, Humanoid humanoid, Room room)
    {
        switch (Phase)
        {
            case DoctorPhase.OtWalkingToScreen:
                if (RoomVisits.FindObject(simulation, room.Id, SurgeonScreen) is not { } screen)
                {
                    Phase = DoctorPhase.OtReady;
                    break;
                }

                if (RoomVisits.IsObjectBusy(simulation, screen, humanoid))
                {
                    // wait_for_object: idle until nobody uses the screen.
                    break;
                }

                // use_screen.lua: the doctor turns into a surgeon; the screen's outfits decide the animation.
                var surgeons = RoomVisits.SurgeonsInScrubs(simulation, room);
                var greens = 2 - surgeons;
                _sequence.Clear();
                _sequence.Add(surgeons > 0 ? (greens > 1 ? new AnimationRef(2780, 33) : new AnimationRef(2784, 40)) : new AnimationRef(2782, 40));
                _sequenceTile = screen.Tile;
                _sequenceDrawTile = RoomVisits.RenderTile(simulation, screen);
                _sequenceObject = screen.Id;
                humanoid.Type = HumanoidType.Surgeon;
                Phase = DoctorPhase.OtChanging;
                PlayNext(simulation, humanoid);
                break;
            case DoctorPhase.OtChanging:
            case DoctorPhase.OtOperating:
                if (--_sequenceTimer > 0)
                {
                    break;
                }

                if (_sequence.Count > 0)
                {
                    PlayNext(simulation, humanoid);
                    break;
                }

                humanoid.ClearActionAnimation();
                _sequenceObject = 0;
                Phase = DoctorPhase.OtReady;
                Meander.Reset();
                break;
            case DoctorPhase.OtReady:
                Meander.Update(simulation, humanoid);
                break;
            case DoctorPhase.OtWalkingToSink:
                if (RoomVisits.FindObject(simulation, room.Id, OperatingSink) is not { } sink)
                {
                    WalkToTablePlace(simulation, humanoid, room);
                    break;
                }

                if (!RoomVisits.IsObjectBusy(simulation, sink, humanoid))
                {
                    Phase = DoctorPhase.OtWashing;
                    Use = ObjectUse.Start(simulation, humanoid, sink);
                }

                break;
            case DoctorPhase.OtWashing:
                if (Use is null || Use.Update(simulation, humanoid) == ObjectUseEvent.Done)
                {
                    Use = null;
                    WalkToTablePlace(simulation, humanoid, room);
                }

                break;
            case DoctorPhase.OtWalkingToTable:
                Phase = DoctorPhase.OtAtTable;
                break;
        }
    }

    /// <summary>The first surgeon (the room's) to the table's patient side, the second to the far side (operating_table_b).</summary>
    private void WalkToTablePlace(Simulation simulation, Humanoid humanoid, Room room)
    {
        Phase = DoctorPhase.OtWalkingToTable;
        if (RoomVisits.FindObject(simulation, room.Id, OperatingTable) is not { } table)
        {
            return;
        }

        humanoid.WalkTo(simulation, room.StaffId == humanoid.Id ? RoomVisits.UseTile(simulation, table) : RoomVisits.OperatingTableSlaveTile(simulation, table));
    }

    private void PlayNext(Simulation simulation, Humanoid humanoid)
    {
        var animation = _sequence[0];
        _sequence.RemoveAt(0);
        humanoid.SetActionAnimation(simulation, animation, false, _sequenceTile, _sequenceDrawTile, _sequenceObject);
        _sequenceTimer = animation.Length;
    }

    /// <summary>GPRoom:doStaffUseCycle: to the cabinet, then the desk.</summary>
    private void StartCycle(Simulation simulation, Humanoid humanoid, Room room)
    {
        Use = null;
        if (RoomVisits.FindObject(simulation, room.Id, CabinetType) is { } cabinet)
        {
            Phase = DoctorPhase.WalkingToCabinet;
            humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, cabinet));
        }
        else
        {
            WalkToDesk(simulation, humanoid, room);
        }
    }

    /// <summary>
    /// <c>GeneralDiagRoom:commandEnteringPatient</c>'s <c>after_use_screen1</c>: the patient has undressed, so the doctor
    /// leaves off meandering and walks to the crash trolley.
    /// </summary>
    internal void OnPatientUndressed(Simulation simulation, Humanoid humanoid, Room room)
    {
        if (Phase == DoctorPhase.Waiting && RoomId == room.Id)
        {
            WalkToTrolley(simulation, humanoid, room);
        }
    }

    /// <summary><c>after_use_trolley</c>: the examination is over and the doctor meanders again.</summary>
    internal void OnExamined(Room room)
    {
        if (Phase == DoctorPhase.AtTrolley && RoomId == room.Id)
        {
            Wait();
        }
        else if (Phase == DoctorPhase.UsingShield && RoomId == room.Id)
        {
            // after_use_xray: the doctor gets up from the console (its use is interrupted) and meanders.
            Use?.Interrupt();
        }
    }

    /// <summary><c>XRayRoom:commandEnteringPatient</c>: both are ready, so the doctor sits at the radiation shield's console.</summary>
    internal void StartShield(Simulation simulation, Humanoid humanoid, Room room)
    {
        if (Phase == DoctorPhase.AtTrolley && RoomId == room.Id && RoomVisits.DoctorConsole(room.RoomType) is { } console
            && RoomVisits.FindObject(simulation, room.Id, console) is { } shield)
        {
            Phase = DoctorPhase.UsingShield;
            Use = ObjectUse.Start(simulation, humanoid, shield);
        }
    }

    /// <summary>Whether the room's patient is on their way to the trolley, waiting at it or lying on it.</summary>
    private static bool IsPatientComingToTrolley(Simulation simulation, Room room) =>
        simulation.FindEntity(room.PatientId) is Humanoid
        {
            Behaviour: PatientBehaviour { Phase: PatientPhase.WalkingToTrolley or PatientPhase.AtTrolley or PatientPhase.OnTrolley },
        };

    /// <summary>Meanders about the room until a patient needs the doctor, starting with a walk.</summary>
    private void Wait()
    {
        Use = null;
        Phase = DoctorPhase.Waiting;
        Meander.Reset();
    }

    /// <summary>GeneralDiagRoom: the doctor waits for the patient at the trolley's secondary use position.</summary>
    private void WalkToTrolley(Simulation simulation, Humanoid humanoid, Room room)
    {
        Use = null;
        Phase = DoctorPhase.WalkingToTrolley;
        if (RoomVisits.DoctorStation(simulation, room) is { } station)
        {
            humanoid.WalkTo(simulation, station);
        }
    }

    private void WalkToDesk(Simulation simulation, Humanoid humanoid, Room room)
    {
        Use = null;
        Phase = DoctorPhase.WalkingToDesk;
        if (RoomVisits.FindObject(simulation, room.Id, DeskType) is { } desk)
        {
            humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, desk));
        }
    }

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteByte((byte)Phase);
        writer.WriteInt32(RoomId);
        writer.WriteInt32(DeskUseTime);
        ObjectUse.WriteOptional(writer, Use);
        Meander.Write(writer);
        writer.WriteInt32(ResearchObjectId);
        writer.WriteInt32(_previousResearchObject);
        writer.WriteInt32(_researchRounds);
        writer.WriteInt32(_researchMeanders);
        writer.WriteInt32(_psychDuration);
        writer.WriteInt32(_sequence.Count);
        foreach (var animation in _sequence)
        {
            writer.WriteInt32(animation.Id);
            writer.WriteInt32(animation.Length);
        }

        writer.WriteInt32(_sequenceTimer);
        writer.WriteTile(_sequenceTile);
        writer.WriteTile(_sequenceDrawTile);
        writer.WriteInt32(_sequenceObject);
        writer.WriteInt32(TrainingObjectId);
        writer.WriteInt32(_trainingRounds);
    }

    internal static DoctorBehaviour ReadState(SimReader reader)
    {
        var doctor = new DoctorBehaviour
        {
            Phase = (DoctorPhase)reader.ReadByte(),
            RoomId = reader.ReadInt32(),
            DeskUseTime = reader.ReadInt32(),
            Use = ObjectUse.ReadOptional(reader),
            Meander = Meandering.Read(reader),
            ResearchObjectId = reader.ReadInt32(),
            _previousResearchObject = reader.ReadInt32(),
            _researchRounds = reader.ReadInt32(),
            _researchMeanders = reader.ReadInt32(),
            _psychDuration = reader.Version is 0 or >= 60 ? reader.ReadInt32() : 0,
        };
        if (reader.Version is 0 or >= 65)
        {
            var count = reader.ReadCount(64);
            for (var i = 0; i < count; i++)
            {
                doctor._sequence.Add(new AnimationRef(reader.ReadInt32(), reader.ReadInt32()));
            }

            doctor._sequenceTimer = reader.ReadInt32();
            doctor._sequenceTile = reader.ReadTile();
            doctor._sequenceDrawTile = reader.ReadTile();
            doctor._sequenceObject = reader.ReadInt32();
        }

        if (reader.Version is 0 or >= 70)
        {
            doctor.TrainingObjectId = reader.ReadInt32();
            doctor._trainingRounds = reader.ReadInt32();
        }

        return doctor;
    }
}
