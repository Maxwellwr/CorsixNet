using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.People;

/// <summary>Identifies the concrete type of a behaviour in serialized state.</summary>
public enum BehaviourKind : byte
{
    None = 0,
    Patient = 1,
    Receptionist = 2,
    Doctor = 3,
    Nurse = 4,

    /// <summary>A member of staff leaving the hospital.</summary>
    Leaving = 5,

    /// <summary>A VIP visiting.</summary>
    Vip = 6,

    /// <summary>The health inspector.</summary>
    Inspector = 7,
}

/// <summary>A member of staff whom a room sends for and who works in it.</summary>
internal interface IRoomStaff
{
    /// <summary>The room they work in or are going to, or 0 when free.</summary>
    int RoomId { get; }

    /// <summary>Sends them to work in <paramref name="room"/>, walking to <paramref name="inside"/> first.</summary>
    void GoToRoom(Simulation simulation, Humanoid humanoid, Room room, TilePosition inside);
}

/// <summary>
/// What a humanoid is doing beyond walking: a patient's visit, a receptionist's or a doctor's job.
/// CorsixTH strings humanoid actions together in an action queue; here each kind of humanoid follows
/// its own small state machine, which is easier to save explicitly.
/// </summary>
/// <remarks>
/// <see cref="Update"/> runs each tick in which the humanoid is not walking.
/// </remarks>
public abstract class HumanoidBehaviour
{
    public abstract BehaviourKind Kind { get; }

    internal abstract void Update(Simulation simulation, Humanoid humanoid);

    protected abstract void WriteState(SimWriter writer);

    internal static void Write(SimWriter writer, HumanoidBehaviour? behaviour)
    {
        writer.WriteByte((byte)(behaviour?.Kind ?? BehaviourKind.None));
        behaviour?.WriteState(writer);
    }

    internal static HumanoidBehaviour? Read(SimReader reader)
    {
        var kind = (BehaviourKind)reader.ReadByte();
        return kind switch
        {
            BehaviourKind.None => null,
            BehaviourKind.Patient => PatientBehaviour.ReadState(reader),
            BehaviourKind.Receptionist => ReceptionistBehaviour.ReadState(reader),
            BehaviourKind.Doctor => DoctorBehaviour.ReadState(reader),
            BehaviourKind.Nurse => NurseBehaviour.ReadState(reader),
            BehaviourKind.Leaving => LeavingBehaviour.ReadState(reader),
            BehaviourKind.Vip => VipBehaviour.ReadState(reader),
            BehaviourKind.Inspector => InspectorBehaviour.ReadState(reader),
            _ => throw new InvalidDataException($"Unknown behaviour kind {(byte)kind}."),
        };
    }
}
