using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Entities;

/// <summary>Identifies the concrete type of an entity in serialized state.</summary>
public enum EntityKind : byte
{
    Humanoid = 1,
    EntranceDoor = 2,
    Room = 3,
    RoomDoor = 4,
    RoomObject = 5,
    ConstructionSite = 6,
}

/// <summary>
/// Something in the world that changes over time. Entities are updated once per tick,
/// in increasing <see cref="Id"/> order.
/// </summary>
public abstract class Entity
{
    protected Entity(int id)
    {
        Id = id;
    }

    /// <summary>Unique, assigned sequentially by the simulation; never reused.</summary>
    public int Id { get; }

    public abstract EntityKind Kind { get; }

    /// <summary>Advances the entity by one tick.</summary>
    public abstract void Update(Simulation simulation);

    /// <summary>Writes the entity, including its kind and id.</summary>
    public void Write(SimWriter writer)
    {
        writer.WriteByte((byte)Kind);
        writer.WriteInt32(Id);
        WriteState(writer);
    }

    /// <summary>Reads an entity written by <see cref="Write"/>.</summary>
    public static Entity Read(SimReader reader)
    {
        var kind = (EntityKind)reader.ReadByte();
        var id = reader.ReadInt32();
        return kind switch
        {
            EntityKind.Humanoid => Humanoid.ReadState(id, reader),
            EntityKind.EntranceDoor => EntranceDoor.ReadState(id, reader),
            EntityKind.Room => Room.ReadState(id, reader),
            EntityKind.RoomDoor => RoomDoor.ReadState(id, reader),
            EntityKind.RoomObject => RoomObject.ReadState(id, reader),
            EntityKind.ConstructionSite => Rooms.ConstructionSite.ReadState(id, reader),
            _ => throw new InvalidDataException($"Unknown entity kind {(byte)kind}."),
        };
    }

    /// <summary>Writes the state specific to the entity type.</summary>
    protected abstract void WriteState(SimWriter writer);
}
