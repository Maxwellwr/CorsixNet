// Ported from CorsixTH CorsixTH/Lua/rooms/pharmacy.lua (PharmacyRoom:commandEnteringPatient) and CorsixTH/Lua/room.lua
// (commandEnteringStaff) and CorsixTH/Lua/rooms/ward.lua (WardRoom:doStaffUseCycle).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.People;

/// <summary>Where a nurse is in their job.</summary>
public enum NursePhase : byte
{
    /// <summary>Without a room.</summary>
    Idle,

    WalkingToRoom,

    /// <summary>Walking to where she uses the room's object.</summary>
    WalkingToObject,

    /// <summary>At the object, waiting for a patient to stand ready.</summary>
    Waiting,

    /// <summary>Using the object with the patient.</summary>
    Treating,

    /// <summary>Walking to a tile next to a patient to vaccinate them, during an epidemic's cover-up.</summary>
    WalkingToVaccinate,

    /// <summary>Standing next to the patient, vaccinating them.</summary>
    Vaccinating,

    /// <summary>In the ward: meandering a number of rounds.</summary>
    WardMeandering,

    /// <summary>In the ward: walking to a free desk.</summary>
    WalkingToWardDesk,

    /// <summary>In the ward: writing at the desk.</summary>
    UsingWardDesk,

    /// <summary>In the fracture clinic: walking to the cast remover's other side.</summary>
    WalkingToStation,

    /// <summary>In the fracture clinic: standing there for the patient, shown in the machine's animations while they use it.</summary>
    AtStation,
}

/// <summary>
/// A nurse working in the pharmacy: she meanders about the room (<c>Room:commandEnteringStaff</c>); when a patient comes
/// in she walks to the cabinet, and once they stand at its other side she hands them a drink (the cabinet's multi-use
/// animations), the room is done with the patient and she meanders again (<c>after_use_pharmacy</c>).
/// </summary>
public sealed class NurseBehaviour : HumanoidBehaviour, IRoomStaff
{
    public override BehaviourKind Kind => BehaviourKind.Nurse;

    public NursePhase Phase { get; private set; }

    /// <summary>The room the nurse works in or is going to, or 0.</summary>
    public int RoomId { get; private set; }

    /// <summary>The nurse's use of the cabinet with a patient, while it lasts.</summary>
    public MultiObjectUse? Use { get; private set; }

    /// <summary>The patient she is going to vaccinate or vaccinating, or 0.</summary>
    public int VaccinatePatientId { get; private set; }

    /// <summary>Wandering about while without a room.</summary>
    private Meandering Meander { get; init; } = new();

    /// <summary>Hours left of vaccinating (the 5 of CorsixTH's idle before <c>perform_vaccination</c>).</summary>
    private int VaccinateTimer { get; set; }

    /// <summary>In the ward: the desk she goes to or writes at (CorsixTH's <c>reserved_for</c>), or 0.</summary>
    public int WardDeskId { get; private set; }

    /// <summary>In the ward: her use of the desk, while it lasts.</summary>
    private ObjectUse? DeskUse { get; set; }

    /// <summary>In the ward: meander rounds left (<c>meander_time</c>, then <c>num_meanders</c>).</summary>
    private int _wardMeanders;

    /// <summary>In the ward: rounds at the desk left (<c>desk_use_time</c>).</summary>
    private int _wardDeskRounds;

    /// <summary>In the ward: whether the meander after finding no free desk is under way (the second meander).</summary>
    private bool _wardSecondMeander;

    /// <summary>Whether she is free for a room or a vaccination: without a room and not vaccinating.</summary>
    internal bool IsFree => Phase == NursePhase.Idle && RoomId == 0;

    void IRoomStaff.GoToRoom(Simulation simulation, Humanoid humanoid, Room room, TilePosition inside)
    {
        Meander.Reset();
        RoomId = room.Id;
        Phase = NursePhase.WalkingToRoom;
        humanoid.WalkTo(simulation, inside);
    }

    internal override void Update(Simulation simulation, Humanoid humanoid)
    {
        if (Phase == NursePhase.Idle)
        {
            if (!RoomVisits.SettleIdleStaff(simulation, humanoid))
            {
                Meander.Update(simulation, humanoid);
            }

            return;
        }

        if (Phase is NursePhase.WalkingToVaccinate or NursePhase.Vaccinating)
        {
            Vaccinate(simulation, humanoid);
            return;
        }

        if (simulation.FindEntity(RoomId) is not Room room)
        {
            Phase = NursePhase.Idle;
            RoomId = 0;
            Use = null;
            return;
        }

        switch (Phase)
        {
            case NursePhase.WalkingToRoom:
                if (!room.Area.Contains(humanoid.Tile))
                {
                    // The walk failed: give the room up, so that it sends for someone else.
                    room.StaffId = 0;
                    RoomId = 0;
                    Phase = NursePhase.Idle;
                    return;
                }

                room.StaffPresent = true;

                // Room:commandEnteringStaff.
                RoomVisits.TryToFindNearbyPatients(simulation, room);
                if (string.Equals(room.RoomType, RoomVisits.WardRoom, StringComparison.Ordinal))
                {
                    StartWardCycle(simulation);
                }
                else
                {
                    Wait();
                }

                break;
            case NursePhase.WardMeandering:
            case NursePhase.WalkingToWardDesk:
            case NursePhase.UsingWardDesk:
                WardStep(simulation, humanoid, room);
                break;
            case NursePhase.WalkingToObject:
                Phase = NursePhase.Waiting;
                break;
            case NursePhase.WalkingToStation:
                Phase = NursePhase.AtStation;
                break;
            case NursePhase.AtStation:
                if (room.PatientId == 0)
                {
                    Wait();
                }

                break;
            case NursePhase.Waiting:
                if (room.PatientId == 0 || string.Equals(room.RoomType, RoomVisits.FractureClinicRoom, StringComparison.Ordinal))
                {
                    Meander.Update(simulation, humanoid);
                }
                else
                {
                    TryTreat(simulation, humanoid, room);
                }

                break;
            case NursePhase.Treating:
                if (Use is null || Use.Update(simulation, humanoid) == ObjectUseEvent.Done)
                {
                    // after_use_pharmacy: the room is done with the patient.
                    Use = null;
                    Wait();
                    RoomVisits.DealtWithPatient(simulation, room);
                }

                break;
        }
    }

    /// <summary>
    /// Once the patient stands ready at the cabinet, the nurse starts the drink with a random flask colour (female
    /// patients cannot have colour 2, which their idle animation uses for a bandage).
    /// </summary>
    private void TryTreat(Simulation simulation, Humanoid humanoid, Room room)
    {
        if (RoomVisits.PatientObject(simulation, room) is not { } cabinet)
        {
            return;
        }

        var useTile = RoomVisits.UseTile(simulation, cabinet);
        if (humanoid.Tile != useTile)
        {
            WalkToObject(simulation, humanoid, room);
            return;
        }

        if (simulation.FindEntity(room.PatientId) is not Humanoid { Behaviour: PatientBehaviour { IsReadyForStaff: true } } patient || patient.IsMoving)
        {
            return;
        }

        var layer3 = patient.Type == HumanoidType.StandardFemalePatient
            ? simulation.Random.NextInclusive(0, 1) * 4
            : simulation.Random.NextInclusive(0, 2) * 2;
        Use = MultiObjectUse.Start(simulation, humanoid, cabinet, patient, layer3);
        if (Use is null)
        {
            RoomVisits.DealtWithPatient(simulation, room);
            return;
        }

        Phase = NursePhase.Treating;
    }

    /// <summary>
    /// <c>PharmacyRoom:commandEnteringPatient</c>: a patient has come in, so the nurse leaves off meandering and walks to
    /// the cabinet.
    /// </summary>
    internal void OnPatientEntered(Simulation simulation, Humanoid humanoid, Room room)
    {
        if (Phase == NursePhase.Waiting && RoomId == room.Id)
        {
            WalkToObject(simulation, humanoid, room);
        }
    }

    /// <summary><c>WardRoom:doStaffUseCycle</c>: meander 4 to 10 rounds before going to a desk.</summary>
    private void StartWardCycle(Simulation simulation)
    {
        DeskUse = null;
        WardDeskId = 0;
        Phase = NursePhase.WardMeandering;
        Meander.Reset();
        _wardMeanders = simulation.Random.NextInclusive(4, 10);
        _wardSecondMeander = false;
    }

    /// <summary>
    /// The ward nurse's cycle: after meandering she writes at a free desk for 7 to 14 rounds and starts again; without a
    /// free desk she meanders 2 to 4 rounds more and starts again.
    /// </summary>
    private void WardStep(Simulation simulation, Humanoid humanoid, Room room)
    {
        switch (Phase)
        {
            case NursePhase.WardMeandering:
                Meander.Update(simulation, humanoid, loop: () =>
                {
                    if (--_wardMeanders > 0)
                    {
                        return false;
                    }

                    if (_wardSecondMeander)
                    {
                        StartWardCycle(simulation);
                        return true;
                    }

                    if (RoomVisits.FindFreeWardDesk(simulation, room, humanoid) is { } desk)
                    {
                        WardDeskId = desk.Id;
                        _wardDeskRounds = simulation.Random.NextInclusive(7, 14);
                        Phase = NursePhase.WalkingToWardDesk;
                        humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, desk));
                    }
                    else
                    {
                        _wardSecondMeander = true;
                        _wardMeanders = simulation.Random.NextInclusive(2, 4);
                    }

                    return true;
                });
                break;
            case NursePhase.WalkingToWardDesk:
                if (simulation.FindEntity(WardDeskId) is RoomObject target && humanoid.Tile == RoomVisits.UseTile(simulation, target))
                {
                    Phase = NursePhase.UsingWardDesk;
                    DeskUse = ObjectUse.Start(simulation, humanoid, target);
                }
                else
                {
                    StartWardCycle(simulation);
                }

                break;
            case NursePhase.UsingWardDesk:
                switch (DeskUse?.Update(simulation, humanoid) ?? ObjectUseEvent.Done)
                {
                    case ObjectUseEvent.Loop:
                        if (--_wardDeskRounds <= 0)
                        {
                            DeskUse!.Interrupt();
                        }

                        break;
                    case ObjectUseEvent.Done:
                        StartWardCycle(simulation);
                        break;
                }

                break;
        }
    }

    /// <summary>
    /// <c>Staff:getServiceQuality</c>: seven tenths her skill, two tenths how rested and a tenth how happy she is, from 0
    /// to 1.
    /// </summary>
    internal static decimal ServiceQuality(Humanoid humanoid) =>
        (0.7m * (humanoid.Profile?.Skill ?? 0) / Staff.StaffProfile.MaxSkill) + (0.2m * (1 - humanoid.Fatigue)) + (0.1m * humanoid.Happiness);

    /// <summary>
    /// <c>FractureRoom:commandEnteringPatient</c>: the patient stands at the cast remover, so the nurse walks to its other
    /// side and waits there.
    /// </summary>
    internal void OnPatientAtMachine(Simulation simulation, Humanoid humanoid, Room room)
    {
        if (Phase == NursePhase.Waiting && RoomId == room.Id && RoomVisits.ExaminingObject(simulation, room) is { } machine)
        {
            Phase = NursePhase.WalkingToStation;
            humanoid.WalkTo(simulation, RoomVisits.SecondaryUseTile(simulation, machine));
        }
    }

    /// <summary>Whether the nurse stands at the machine for the patient.</summary>
    public bool IsAtStation => Phase == NursePhase.AtStation;

    /// <summary><c>fracture_clinic_after_use</c>: the nurse meanders again.</summary>
    internal void OnExamined(Room room)
    {
        if (Phase == NursePhase.AtStation && RoomId == room.Id)
        {
            Wait();
        }
    }

    /// <summary>Waits for a patient, meandering about the room, starting with a walk.</summary>
    private void Wait()
    {
        Phase = NursePhase.Waiting;
        Meander.Reset();
    }

    /// <summary>
    /// <c>Epidemic:createVaccinationActions</c>: the nurse walks to the tile chosen next to the patient, who stays reserved
    /// for her.
    /// </summary>
    internal void StartVaccination(Simulation simulation, Humanoid humanoid, Humanoid patient, TilePosition tile)
    {
        Meander.Reset();
        VaccinatePatientId = patient.Id;
        Phase = NursePhase.WalkingToVaccinate;
        humanoid.WalkTo(simulation, tile);
    }

    /// <summary>
    /// <c>VaccinateAction</c>: next to the patient, both out of rooms, the nurse faces them for 5 hours; if they are still
    /// next to each other the patient is vaccinated and the hospital pays the fee. Otherwise, or if she could not get
    /// there, the patient is free to call again.
    /// </summary>
    private void Vaccinate(Simulation simulation, Humanoid humanoid)
    {
        if (humanoid.IsMoving)
        {
            return;
        }

        if (simulation.FindEntity(VaccinatePatientId) is not Humanoid { Behaviour: PatientBehaviour patient } target || patient.IsVaccinated)
        {
            // The patient is gone, or the epidemic ended and everyone counts as vaccinated: nothing to do or pay.
            EndVaccination(humanoid, null);
            return;
        }

        if (!Disasters.Epidemics.AreNextToEachOtherOutside(simulation, humanoid, target))
        {
            EndVaccination(humanoid, patient);
            return;
        }

        if (Phase == NursePhase.WalkingToVaccinate)
        {
            Phase = NursePhase.Vaccinating;
            VaccinateTimer = 5;
            humanoid.FaceTowards(target.Tile);
            return;
        }

        if (--VaccinateTimer > 0)
        {
            return;
        }

        if (target.HospitalPlayer >= 0 && target.HospitalPlayer < simulation.Hospitals.Count)
        {
            Disasters.Epidemics.Vaccinated(simulation, simulation.Hospitals[target.HospitalPlayer], patient);
        }

        EndVaccination(humanoid, patient);
    }

    private void EndVaccination(Humanoid humanoid, PatientBehaviour? patient)
    {
        if (patient is not null && patient.VaccinatingNurseId == humanoid.Id)
        {
            patient.VaccinatingNurseId = 0;
        }

        VaccinatePatientId = 0;
        VaccinateTimer = 0;
        Phase = NursePhase.Idle;
        Meander.Reset();
    }

    private void WalkToObject(Simulation simulation, Humanoid humanoid, Room room)
    {
        Phase = NursePhase.WalkingToObject;
        if (RoomVisits.PatientObject(simulation, room) is { } cabinet)
        {
            humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, cabinet));
        }
    }

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteByte((byte)Phase);
        writer.WriteInt32(RoomId);
        MultiObjectUse.WriteOptional(writer, Use);
        writer.WriteInt32(VaccinatePatientId);
        writer.WriteInt32(VaccinateTimer);
        Meander.Write(writer);
        writer.WriteInt32(WardDeskId);
        ObjectUse.WriteOptional(writer, DeskUse);
        writer.WriteInt32(_wardMeanders);
        writer.WriteInt32(_wardDeskRounds);
        writer.WriteBool(_wardSecondMeander);
    }

    internal static NurseBehaviour ReadState(SimReader reader)
    {
        var nurse = new NurseBehaviour
        {
            Phase = (NursePhase)reader.ReadByte(),
            RoomId = reader.ReadInt32(),
            Use = MultiObjectUse.ReadOptional(reader),
            VaccinatePatientId = reader.ReadInt32(),
            VaccinateTimer = reader.ReadInt32(),
            Meander = Meandering.Read(reader),
        };
        if (reader.Version is 0 or >= 61)
        {
            nurse.WardDeskId = reader.ReadInt32();
            nurse.DeskUse = ObjectUse.ReadOptional(reader);
            nurse._wardMeanders = reader.ReadInt32();
            nurse._wardDeskRounds = reader.ReadInt32();
            nurse._wardSecondMeander = reader.ReadBool();
        }

        return nurse;
    }
}
