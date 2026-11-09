// Ported from CorsixTH CorsixTH/Lua/rooms/gp.lua (GPRoom:doStaffUseCycle, commandEnteringStaff) and
// CorsixTH/Lua/rooms/general_diag.lua (GeneralDiagRoom:commandEnteringPatient, the doctor's part).
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
        };
    }
}
