using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Commands;

/// <summary>Identifies the concrete type of a command in serialized form.</summary>
public enum CommandKind : ushort
{
    SpawnHumanoid = 1,
    WalkTo = 2,
    BuildRoom = 3,
    PlaceObject = 4,
    PickUpObject = 5,
    ConfirmRoom = 6,
    CancelRoom = 7,
}

/// <summary>
/// A player action. Commands are the only input to the simulation: in a lockstep game,
/// every machine executes the same commands on the same ticks.
/// </summary>
public abstract record Command
{
    public abstract CommandKind Kind { get; }

    /// <summary>Applies the command. Invalid commands (for example naming a missing entity) do nothing.</summary>
    public abstract void Execute(Simulation simulation, int player);

    public void Write(SimWriter writer)
    {
        writer.WriteUInt16((ushort)Kind);
        WriteArguments(writer);
    }

    public static Command Read(SimReader reader)
    {
        var kind = (CommandKind)reader.ReadUInt16();
        return kind switch
        {
            CommandKind.SpawnHumanoid => new SpawnHumanoidCommand(reader.ReadTile()),
            CommandKind.WalkTo => new WalkToCommand(reader.ReadInt32(), reader.ReadTile()),
            CommandKind.BuildRoom => BuildRoomCommand.ReadArguments(reader),
            CommandKind.PlaceObject => new PlaceObjectCommand(reader.ReadString(), reader.ReadTile(), (Direction)reader.ReadByte()),
            CommandKind.PickUpObject => new PickUpObjectCommand(reader.ReadInt32()),
            CommandKind.ConfirmRoom => new ConfirmRoomCommand(reader.ReadInt32()),
            CommandKind.CancelRoom => new CancelRoomCommand(reader.ReadInt32()),
            _ => throw new InvalidDataException($"Unknown command kind {(ushort)kind}."),
        };
    }

    protected abstract void WriteArguments(SimWriter writer);
}

/// <summary>Debug command: places a patient with a random appearance on a passable tile.</summary>
public sealed record SpawnHumanoidCommand(TilePosition Tile) : Command
{
    public override CommandKind Kind => CommandKind.SpawnHumanoid;

    public override void Execute(Simulation simulation, int player)
    {
        if (simulation.Map.Contains(Tile.X, Tile.Y) && simulation.Map[Tile.X, Tile.Y].Has(TileFlags.Passable))
        {
            simulation.AddEntity(id => Humanoid.CreateRandomPatient(id, Tile, simulation.Random));
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteTile(Tile);
}

/// <summary>Debug command: sends a humanoid walking to a tile.</summary>
public sealed record WalkToCommand(int HumanoidId, TilePosition Target) : Command
{
    public override CommandKind Kind => CommandKind.WalkTo;

    public override void Execute(Simulation simulation, int player)
    {
        if (simulation.FindEntity(HumanoidId) is Humanoid humanoid)
        {
            humanoid.WalkTo(simulation, Target);
        }
    }

    protected override void WriteArguments(SimWriter writer)
    {
        writer.WriteInt32(HumanoidId);
        writer.WriteTile(Target);
    }
}

/// <summary>
/// Builds a room, if the plan is valid for the issuing player when the command executes. The room
/// starts inactive: its owner furnishes it, then confirms or cancels it.
/// </summary>
public sealed record BuildRoomCommand(RoomPlan Plan) : Command
{
    public override CommandKind Kind => CommandKind.BuildRoom;

    public override void Execute(Simulation simulation, int player)
    {
        if (RoomBuilder.Check(simulation.Map, simulation.Content, Plan, player) == RoomPlanProblem.None)
        {
            RoomBuilder.Build(simulation, Plan, simulation.Content.FindRoom(Plan.RoomType)!);
        }
    }

    internal static BuildRoomCommand ReadArguments(SimReader reader)
    {
        var roomType = reader.ReadString();
        var area = new TileRect(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        var door = ReadEdge(reader);
        var windows = new DoorPlacement[reader.ReadCount(RoomPlan.MaxWindows)];
        for (var i = 0; i < windows.Length; i++)
        {
            windows[i] = ReadEdge(reader);
        }

        return new BuildRoomCommand(new RoomPlan(roomType, area, door, windows));
    }

    protected override void WriteArguments(SimWriter writer)
    {
        writer.WriteString(Plan.RoomType);
        writer.WriteInt32(Plan.Area.X);
        writer.WriteInt32(Plan.Area.Y);
        writer.WriteInt32(Plan.Area.Width);
        writer.WriteInt32(Plan.Area.Height);
        WriteEdge(writer, Plan.Door);
        writer.WriteInt32(Plan.WindowList.Count);
        foreach (var window in Plan.WindowList)
        {
            WriteEdge(writer, window);
        }
    }

    private static DoorPlacement ReadEdge(SimReader reader) => new(reader.ReadTile(), (EdgeOrientation)reader.ReadByte());

    private static void WriteEdge(SimWriter writer, DoorPlacement edge)
    {
        writer.WriteTile(edge.Tile);
        writer.WriteByte((byte)edge.Edge);
    }
}

/// <summary>Places an object, if the placement is valid for the issuing player when the command executes.</summary>
public sealed record PlaceObjectCommand(string ObjectType, TilePosition Tile, Direction Orientation) : Command
{
    public override CommandKind Kind => CommandKind.PlaceObject;

    public override void Execute(Simulation simulation, int player)
    {
        if (ObjectPlacer.Check(simulation, ObjectType, Tile, Orientation, player) == ObjectPlacementProblem.None)
        {
            ObjectPlacer.Place(simulation, simulation.Content.FindObject(ObjectType)!, Tile, Orientation);
        }
    }

    protected override void WriteArguments(SimWriter writer)
    {
        writer.WriteString(ObjectType);
        writer.WriteTile(Tile);
        writer.WriteByte((byte)Orientation);
    }
}

/// <summary>Picks an object up from a room that is still being built, if it belongs to the issuing player.</summary>
/// <remarks>The client then lets the player place the object again, with a <see cref="PlaceObjectCommand"/>.</remarks>
public sealed record PickUpObjectCommand(int ObjectId) : Command
{
    public override CommandKind Kind => CommandKind.PickUpObject;

    public override void Execute(Simulation simulation, int player)
    {
        if (ObjectPlacer.CanPickUp(simulation, ObjectId, player))
        {
            ObjectPlacer.PickUp(simulation, (RoomObject)simulation.FindEntity(ObjectId)!);
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteInt32(ObjectId);
}

/// <summary>Finishes a room: once its needed objects stand in it, it starts working.</summary>
public sealed record ConfirmRoomCommand(int RoomId) : Command
{
    public override CommandKind Kind => CommandKind.ConfirmRoom;

    public override void Execute(Simulation simulation, int player)
    {
        if (simulation.FindEntity(RoomId) is Room room && RoomBuilder.CanConfirm(simulation, room, player))
        {
            room.IsActive = true;
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteInt32(RoomId);
}

/// <summary>Removes a room that is still being built, with its objects, door and walls.</summary>
public sealed record CancelRoomCommand(int RoomId) : Command
{
    public override CommandKind Kind => CommandKind.CancelRoom;

    public override void Execute(Simulation simulation, int player)
    {
        if (simulation.FindEntity(RoomId) is Room { IsActive: false } room && RoomBuilder.IsOwnedBy(simulation.Map, room, player))
        {
            RoomBuilder.Remove(simulation, room);
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteInt32(RoomId);
}

/// <summary>A command stamped with the tick it executes on and its order within that tick.</summary>
/// <param name="Tick">Tick on which the command executes, before entities update.</param>
/// <param name="Sequence">Order among commands of the same tick; assigned by whoever schedules commands.</param>
/// <param name="Player">The player issuing the command (0-based).</param>
/// <param name="Command">The command itself.</param>
public sealed record ScheduledCommand(long Tick, long Sequence, int Player, Command Command)
{
    public void Write(SimWriter writer)
    {
        writer.WriteInt64(Tick);
        writer.WriteInt64(Sequence);
        writer.WriteInt32(Player);
        Command.Write(writer);
    }

    public static ScheduledCommand Read(SimReader reader)
    {
        return new ScheduledCommand(reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt32(), Command.Read(reader));
    }
}
