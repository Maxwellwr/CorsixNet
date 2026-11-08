using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.People;

/// <summary>Identifies the concrete type of a behaviour in serialized state.</summary>
public enum BehaviourKind : byte
{
    None = 0,
    Patient = 1,
    Receptionist = 2,
    Doctor = 3,
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
            _ => throw new InvalidDataException($"Unknown behaviour kind {(byte)kind}."),
        };
    }
}
