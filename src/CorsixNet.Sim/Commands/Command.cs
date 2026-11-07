using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Commands;

/// <summary>Identifies the concrete type of a command in serialized form.</summary>
public enum CommandKind : ushort
{
    SpawnHumanoid = 1,
    WalkTo = 2,
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
