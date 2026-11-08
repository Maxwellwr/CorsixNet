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
}

/// <summary>
/// A nurse working in the pharmacy: she waits at the cabinet; when a patient stands at its other side, she hands them a
/// drink (the cabinet's multi-use animations), and the room is done with the patient.
/// </summary>
/// <remarks>
/// CorsixTH's nurse meanders about the room between patients; meandering is not ported yet, so she waits at the
/// cabinet instead.
/// </remarks>
public sealed class NurseBehaviour : HumanoidBehaviour, IRoomStaff
{
    public override BehaviourKind Kind => BehaviourKind.Nurse;

    public NursePhase Phase { get; private set; }

    /// <summary>The room the nurse works in or is going to, or 0.</summary>
    public int RoomId { get; private set; }

    /// <summary>The nurse's use of the cabinet with a patient, while it lasts.</summary>
    public MultiObjectUse? Use { get; private set; }

    void IRoomStaff.GoToRoom(Simulation simulation, Humanoid humanoid, Room room, TilePosition inside)
    {
        RoomId = room.Id;
        Phase = NursePhase.WalkingToRoom;
        humanoid.WalkTo(simulation, inside);
    }

    internal override void Update(Simulation simulation, Humanoid humanoid)
    {
        if (Phase == NursePhase.Idle)
        {
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
                WalkToObject(simulation, humanoid, room);
                break;
            case NursePhase.WalkingToObject:
                Phase = NursePhase.Waiting;
                break;
            case NursePhase.Waiting:
                TryTreat(simulation, humanoid, room);
                break;
            case NursePhase.Treating:
                if (Use is null || Use.Update(simulation, humanoid) == ObjectUseEvent.Done)
                {
                    // after_use_pharmacy: the room is done with the patient.
                    Use = null;
                    Phase = NursePhase.Waiting;
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
    }

    internal static NurseBehaviour ReadState(SimReader reader)
    {
        return new NurseBehaviour
        {
            Phase = (NursePhase)reader.ReadByte(),
            RoomId = reader.ReadInt32(),
            Use = MultiObjectUse.ReadOptional(reader),
        };
    }
}
