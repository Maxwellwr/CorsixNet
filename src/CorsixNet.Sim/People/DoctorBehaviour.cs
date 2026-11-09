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
}

/// <summary>
/// A doctor working in the GP's office: they go back and forth between the filing cabinet and the desk.
/// At the desk, every round of the desk animation counts down; when a patient sits on the chair the doctor
/// may conclude the consultation, the more likely the more skilled they are. In general diagnosis the doctor
/// waits at the crash trolley, where the patient's use of it shows them both.
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

    void IRoomStaff.GoToRoom(Simulation simulation, Humanoid humanoid, Room room, TilePosition inside)
    {
        RoomId = room.Id;
        Phase = DoctorPhase.WalkingToRoom;
        humanoid.WalkTo(simulation, inside);
    }

    internal override void Update(Simulation simulation, Humanoid humanoid)
    {
        if (Phase == DoctorPhase.Idle)
        {
            return;
        }

        if (simulation.FindEntity(RoomId) is not Room room)
        {
            Phase = DoctorPhase.Idle;
            RoomId = 0;
            Use = null;
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
                if (string.Equals(room.RoomType, RoomVisits.GeneralDiagnosisRoom, StringComparison.Ordinal))
                {
                    WalkToTrolley(simulation, humanoid, room);
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
    /// GeneralDiagRoom: the doctor waits for the patient at the trolley's secondary use position. CorsixTH's doctor
    /// meanders about the room until the patient has undressed; meandering is not ported, so they wait there.
    /// </summary>
    private void WalkToTrolley(Simulation simulation, Humanoid humanoid, Room room)
    {
        Use = null;
        Phase = DoctorPhase.WalkingToTrolley;
        if (RoomVisits.FindObject(simulation, room.Id, RoomVisits.TrolleyType) is { } trolley)
        {
            humanoid.WalkTo(simulation, RoomVisits.SecondaryUseTile(simulation, trolley));
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
    }

    internal static DoctorBehaviour ReadState(SimReader reader)
    {
        return new DoctorBehaviour
        {
            Phase = (DoctorPhase)reader.ReadByte(),
            RoomId = reader.ReadInt32(),
            DeskUseTime = reader.ReadInt32(),
            Use = ObjectUse.ReadOptional(reader),
        };
    }
}
