// Ported from CorsixTH CorsixTH/Lua/rooms/gp.lua (GPRoom:doStaffUseCycle, commandEnteringStaff) and
// CorsixTH/Lua/rooms/general_diag.lua (GeneralDiagRoom:commandEnteringPatient, the doctor's part) and
// CorsixTH/Lua/rooms/psych.lua (PsychRoom:commandEnteringStaff, commandEnteringPatient).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

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

    void IRoomStaff.GoToRoom(Simulation simulation, Humanoid humanoid, Room room, TilePosition inside)
    {
        Meander.Reset();
        RoomId = room.Id;
        Phase = DoctorPhase.WalkingToRoom;
        humanoid.WalkTo(simulation, inside);
    }

    internal override void Update(Simulation simulation, Humanoid humanoid)
    {
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
        if (Phase == DoctorPhase.AtTrolley && RoomId == room.Id && RoomVisits.FindObject(simulation, room.Id, "radiation_shield") is { } shield)
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
    }

    internal static DoctorBehaviour ReadState(SimReader reader)
    {
        return new DoctorBehaviour
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
    }
}
