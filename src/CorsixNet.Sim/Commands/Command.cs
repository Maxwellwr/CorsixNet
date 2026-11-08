using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Serialization;
using CorsixNet.Sim.Staff;

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
    HireStaff = 8,
    Meander = 9,
    MarkBlueprint = 10,
    UnmarkBlueprint = 11,
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
            CommandKind.HireStaff => new HireStaffCommand(ReadStaffClass(reader), reader.ReadInt32(), reader.ReadTile()),
            CommandKind.Meander => new MeanderCommand(reader.ReadInt32()),
            CommandKind.MarkBlueprint => new MarkBlueprintCommand(reader.ReadString(), new TileRect(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32())),
            CommandKind.UnmarkBlueprint => new UnmarkBlueprintCommand(),
            _ => throw new InvalidDataException($"Unknown command kind {(ushort)kind}."),
        };
    }

    protected abstract void WriteArguments(SimWriter writer);

    private static StaffClass ReadStaffClass(SimReader reader)
    {
        var staffClass = reader.ReadByte();
        return staffClass < StaffClasses.Count ? (StaffClass)staffClass : throw new InvalidDataException($"Unknown staff class {staffClass}.");
    }
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
/// Debug command: sends a humanoid wandering, as CorsixTH's meander action (<c>humanoid_actions/meander.lua</c>): to one
/// of the 24 nearest idle tiles, picked at random. The idle tile search does not go through doors, so wandering people
/// stay out of rooms (and inside the room they are in).
/// </summary>
public sealed record MeanderCommand(int HumanoidId) : Command
{
    public override CommandKind Kind => CommandKind.Meander;

    public override void Execute(Simulation simulation, int player)
    {
        if (simulation.FindEntity(HumanoidId) is not Humanoid humanoid)
        {
            return;
        }

        var start = humanoid.StepTarget;
        if (simulation.Pathfinder.TryFindIdleTile(simulation.Map, start, simulation.Random.NextInclusive(1, 24), out var tile) && tile != start)
        {
            humanoid.WalkTo(simulation, tile);
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteInt32(HumanoidId);
}

/// <summary>
/// The player is done with a blueprint's area and goes on to its door: its tiles are marked for people to keep off
/// and those on them are told to leave, as CorsixTH's <c>UIEditRoom:_enterDoorPhase</c> does. Replaces the player's
/// previous blueprint, if any. Ignored if the area cannot be built on by the player.
/// </summary>
public sealed record MarkBlueprintCommand(string RoomType, TileRect Area) : Command
{
    public override CommandKind Kind => CommandKind.MarkBlueprint;

    public override void Execute(Simulation simulation, int player)
    {
        if (ConstructionSite.Of(simulation, player) is { IsWaiting: true })
        {
            return;
        }

        if (RoomBuilder.CheckArea(simulation.Map, simulation.Content.FindRoom(RoomType), Area, player) == RoomPlanProblem.None)
        {
            ConstructionSite.Mark(simulation, player, Area);
        }
    }

    protected override void WriteArguments(SimWriter writer)
    {
        writer.WriteString(RoomType);
        writer.WriteInt32(Area.X);
        writer.WriteInt32(Area.Y);
        writer.WriteInt32(Area.Width);
        writer.WriteInt32(Area.Height);
    }
}

/// <summary>
/// The player goes back from a blueprint's door or windows to its area, or gives up while waiting for people to
/// leave it: the tiles are no longer avoided (CorsixTH's <c>UIEditRoom:returnToWallPhase</c>).
/// </summary>
public sealed record UnmarkBlueprintCommand : Command
{
    public override CommandKind Kind => CommandKind.UnmarkBlueprint;

    public override void Execute(Simulation simulation, int player) => ConstructionSite.Of(simulation, player)?.Unmark(simulation);

    protected override void WriteArguments(SimWriter writer)
    {
    }
}

/// <summary>
/// Builds a room, if the plan is valid for the issuing player when the command executes and their
/// hospital can afford the whole room. As in CorsixTH, the room is built once the people on its
/// blueprint have left (<see cref="ConstructionSite"/>), at once if nobody is in the way. The objects
/// it needs are bought then. The room starts inactive: its owner furnishes it, then confirms it
/// (paying for the room) or cancels it (selling the objects back).
/// </summary>
public sealed record BuildRoomCommand(RoomPlan Plan) : Command
{
    public override CommandKind Kind => CommandKind.BuildRoom;

    public override void Execute(Simulation simulation, int player)
    {
        if (RoomBuilder.Check(simulation.Map, simulation.Content, Plan, player) != RoomPlanProblem.None
            || simulation.Content.FindRoom(Plan.RoomType) is not { } room
            || !RoomPurchases.CanAfford(simulation, room, player))
        {
            return;
        }

        var site = ConstructionSite.Of(simulation, player);
        if (site is { IsWaiting: true })
        {
            return;
        }

        if (site is null || site.Area != Plan.Area)
        {
            site = ConstructionSite.Mark(simulation, player, Plan.Area);
        }

        site.Complete(simulation, Plan);
    }

    internal static BuildRoomCommand ReadArguments(SimReader reader) => new(RoomPlanSerializer.Read(reader));

    protected override void WriteArguments(SimWriter writer) => RoomPlanSerializer.Write(writer, Plan);
}

/// <summary>
/// Places an object, if the placement is valid for the issuing player when the command executes. An object
/// for a corridor, such as a reception desk, is bought here, and only if the hospital can afford it.
/// </summary>
public sealed record PlaceObjectCommand(string ObjectType, TilePosition Tile, Direction Orientation) : Command
{
    public override CommandKind Kind => CommandKind.PlaceObject;

    public override void Execute(Simulation simulation, int player)
    {
        if (ObjectPlacer.Check(simulation, ObjectType, Tile, Orientation, player) != ObjectPlacementProblem.None)
        {
            return;
        }

        // Objects in rooms were bought with the room; corridor objects are bought as they are placed.
        var corridor = simulation.Map[Tile.X, Tile.Y].RoomId == 0;
        if (corridor && !RoomPurchases.CanAffordObject(simulation, ObjectType, player))
        {
            return;
        }

        ObjectPlacer.Place(simulation, simulation.Content.FindObject(ObjectType)!, Tile, Orientation);
        if (corridor)
        {
            RoomPurchases.BuyObject(simulation, ObjectType, player);
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
            if (simulation.Content.FindRoom(room.RoomType) is { } definition)
            {
                RoomPurchases.PayForRoom(simulation, definition, player);
            }

            RoomVisits.OnRoomActivated(simulation, room);
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
            if (simulation.Content.FindRoom(room.RoomType) is { } definition)
            {
                RoomPurchases.SellNeededObjects(simulation, definition, player);
            }
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteInt32(RoomId);
}

/// <summary>
/// Hires candidate <paramref name="Index"/> of the list for <paramref name="Class"/> and places them
/// on <paramref name="Tile"/>, if the issuing player's hospital can afford a month's salary and the
/// tile is a valid place when the command executes. Hiring pays the first month's salary.
/// </summary>
/// <remarks>
/// CorsixTH takes the candidate off the list when the player clicks "hire" and puts them back at the
/// end of the list if placing is cancelled. Here choosing and placing are one command, so the list
/// only changes when someone is actually hired.
/// </remarks>
public sealed record HireStaffCommand(StaffClass Class, int Index, TilePosition Tile) : Command
{
    public override CommandKind Kind => CommandKind.HireStaff;

    public override void Execute(Simulation simulation, int player)
    {
        if (StaffHiring.Check(simulation, Class, Index, Tile, player) == StaffHireProblem.None)
        {
            StaffHiring.Hire(simulation, Class, Index, Tile, player);
        }
    }

    protected override void WriteArguments(SimWriter writer)
    {
        writer.WriteByte((byte)Class);
        writer.WriteInt32(Index);
        writer.WriteTile(Tile);
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
