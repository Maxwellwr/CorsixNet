// Ported from CorsixTH CorsixTH/Lua/rooms/pharmacy.lua (PharmacyRoom:commandEnteringPatient) and CorsixTH/Lua/room.lua
// (commandEnteringStaff).
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
            Meander.Update(simulation, humanoid);
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
                Wait();
                break;
            case NursePhase.WalkingToObject:
                Phase = NursePhase.Waiting;
                break;
            case NursePhase.Waiting:
                if (room.PatientId == 0)
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
    }

    internal static NurseBehaviour ReadState(SimReader reader)
    {
        return new NurseBehaviour
        {
            Phase = (NursePhase)reader.ReadByte(),
            RoomId = reader.ReadInt32(),
            Use = MultiObjectUse.ReadOptional(reader),
            VaccinatePatientId = reader.ReadInt32(),
            VaccinateTimer = reader.ReadInt32(),
            Meander = Meandering.Read(reader),
        };
    }
}
