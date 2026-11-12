using CorsixNet.Sim.Diseases;
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
    OpenHospital = 12,
    Borrow = 13,
    Repay = 14,
    PurchaseParcel = 15,
    SetTreatmentPrice = 16,
    SetRadiatorHeat = 17,
    Cheat = 18,
    SetPolicy = 19,
    FireStaff = 20,
    AnswerRaise = 21,
    AnswerVip = 22,
    AnswerEmergency = 23,
    AnswerEpidemic = 24,
    Vaccinate = 25,
    AnswerPatient = 26,
    ShootRat = 27,
    SendPatientHome = 28,
    GuessPatientCure = 29,
    ChangeQueueSize = 30,
    MoveInQueue = 31,
    SendPatientToRoom = 32,
    SetHandymanParcel = 33,
    MoveStaff = 34,
    AdjustResearch = 35,
    ConcentrateResearch = 36,
    PayBonus = 37,
    IncreaseSalary = 38,
    EditRoom = 39,
    CallHandyman = 40,
    ReplaceMachine = 41,
    RemoveDestroyedRoom = 42,
    ReturnToBlueprint = 43,
    AbandonRebuild = 44,
    IncreaseHandymanPriority = 45,
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
            CommandKind.EditRoom => new EditRoomCommand(reader.ReadInt32()),
            CommandKind.HireStaff => new HireStaffCommand(ReadStaffClass(reader), reader.ReadInt32(), reader.ReadTile()),
            CommandKind.Meander => new MeanderCommand(reader.ReadInt32()),
            CommandKind.MarkBlueprint => new MarkBlueprintCommand(reader.ReadString(), new TileRect(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32())),
            CommandKind.UnmarkBlueprint => new UnmarkBlueprintCommand(),
            CommandKind.OpenHospital => new OpenHospitalCommand(),
            CommandKind.Borrow => new BorrowCommand(reader.ReadBool()),
            CommandKind.Repay => new RepayCommand(reader.ReadBool()),
            CommandKind.PurchaseParcel => new PurchaseParcelCommand(reader.ReadInt32()),
            CommandKind.Cheat => new CheatCommand(ReadCheat(reader)),
            CommandKind.SetRadiatorHeat => new SetRadiatorHeatCommand(reader.ReadInt32()),
            CommandKind.SetPolicy => new SetPolicyCommand(ReadPolicy(reader), reader.ReadInt32()),
            CommandKind.FireStaff => new FireStaffCommand(reader.ReadInt32()),
            CommandKind.AnswerRaise => new AnswerRaiseCommand(reader.ReadInt32(), reader.ReadBool()),
            CommandKind.AnswerVip => new AnswerVipCommand(reader.ReadBool()),
            CommandKind.AnswerEmergency => new AnswerEmergencyCommand(reader.ReadBool()),
            CommandKind.AnswerEpidemic => new AnswerEpidemicCommand(reader.ReadBool()),
            CommandKind.Vaccinate => new VaccinateCommand(reader.ReadInt32()),
            CommandKind.AnswerPatient => new AnswerPatientCommand(reader.ReadInt32(), ReadPatientAnswer(reader)),
            CommandKind.ShootRat => new ShootRatCommand(reader.ReadInt32()),
            CommandKind.SendPatientHome => new SendPatientHomeCommand(reader.ReadInt32()),
            CommandKind.GuessPatientCure => new GuessPatientCureCommand(reader.ReadInt32()),
            CommandKind.ChangeQueueSize => new ChangeQueueSizeCommand(reader.ReadInt32(), reader.ReadInt32()),
            CommandKind.MoveInQueue => new MoveInQueueCommand(reader.ReadInt32(), reader.ReadInt32()),
            CommandKind.SendPatientToRoom => new SendPatientToRoomCommand(reader.ReadInt32(), reader.ReadInt32()),
            CommandKind.SetHandymanParcel => new SetHandymanParcelCommand(reader.ReadInt32(), reader.ReadInt32()),
            CommandKind.MoveStaff => new MoveStaffCommand(reader.ReadInt32(), reader.ReadTile()),
            CommandKind.SetTreatmentPrice => new SetTreatmentPriceCommand(reader.ReadString(), reader.ReadInt32()),
            CommandKind.AdjustResearch => new AdjustResearchCommand(ReadResearchCategory(reader), reader.ReadInt32()),
            CommandKind.ConcentrateResearch => new ConcentrateResearchCommand(reader.ReadString()),
            CommandKind.PayBonus => new PayBonusCommand(reader.ReadInt32()),
            CommandKind.IncreaseSalary => new IncreaseSalaryCommand(reader.ReadInt32()),
            CommandKind.CallHandyman => new CallHandymanCommand(reader.ReadInt32()),
            CommandKind.ReplaceMachine => new ReplaceMachineCommand(reader.ReadInt32()),
            CommandKind.RemoveDestroyedRoom => new RemoveDestroyedRoomCommand(reader.ReadInt32()),
            CommandKind.ReturnToBlueprint => new ReturnToBlueprintCommand(reader.ReadInt32()),
            CommandKind.AbandonRebuild => new AbandonRebuildCommand(),
            CommandKind.IncreaseHandymanPriority => new IncreaseHandymanPriorityCommand(reader.ReadInt32(), (HandymanTaskType)reader.ReadByte()),
            _ => throw new InvalidDataException($"Unknown command kind {(ushort)kind}."),
        };
    }

    protected abstract void WriteArguments(SimWriter writer);

    private static CheatKind ReadCheat(SimReader reader)
    {
        var cheat = reader.ReadByte();
        return cheat is >= (byte)CheatKind.Money and <= (byte)CheatKind.ToggleInvulnerableMachines ? (CheatKind)cheat : throw new InvalidDataException($"Unknown cheat {cheat}.");
    }

    private static Research.ResearchCategory ReadResearchCategory(SimReader reader)
    {
        var category = reader.ReadByte();
        return category <= (byte)Research.ResearchCategory.Specialisation ? (Research.ResearchCategory)category : throw new InvalidDataException($"Unknown research category {category}.");
    }

    private static PatientAnswer ReadPatientAnswer(SimReader reader)
    {
        var answer = reader.ReadByte();
        return answer is >= (byte)PatientAnswer.SendHome and <= (byte)PatientAnswer.Research ? (PatientAnswer)answer : throw new InvalidDataException($"Unknown patient answer {answer}.");
    }

    private static PolicyKind ReadPolicy(SimReader reader)
    {
        var policy = reader.ReadByte();
        return policy is >= (byte)PolicyKind.SendHome and <= (byte)PolicyKind.GrantWageIncrease ? (PolicyKind)policy : throw new InvalidDataException($"Unknown policy {policy}.");
    }

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
/// The player opens their hospital before the opening watch has run down, as clicking the button on CorsixTH's
/// watch does (<c>UIWatch:onCountdownEnd</c>).
/// </summary>
public sealed record OpenHospitalCommand : Command
{
    public override CommandKind Kind => CommandKind.OpenHospital;

    public override void Execute(Simulation simulation, int player)
    {
        if (player >= 0 && player < simulation.Hospitals.Count)
        {
            simulation.Hospitals[player].Open();
        }
    }

    protected override void WriteArguments(SimWriter writer)
    {
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
/// Builds a room, if the plan is valid for the issuing player when the command executes, their hospital
/// has discovered the room type and can afford the whole room. As in CorsixTH, the room is built once the people on its
/// blueprint have left (<see cref="ConstructionSite"/>), at once if nobody is in the way. The objects
/// it needs are bought then. The room starts inactive: its owner furnishes it, then confirms it
/// (paying for the room) or cancels it (selling the objects back).
/// </summary>
public sealed record BuildRoomCommand(RoomPlan Plan) : Command
{
    public override CommandKind Kind => CommandKind.BuildRoom;

    public override void Execute(Simulation simulation, int player)
    {
        // A room taken back to its blueprint is paid for already, as CorsixTH edits a room without a money check.
        var rebuilding = player >= 0 && player < simulation.Hospitals.Count && simulation.Hospitals[player].Rebuild is { RoomId: 0 } rebuild
            && rebuild.RoomType == Plan.RoomType;
        if (RoomBuilder.Check(simulation.Map, simulation.Content, Plan, player) != RoomPlanProblem.None
            || simulation.Content.FindRoom(Plan.RoomType) is not { } room
            || !RoomPurchases.CanBuild(simulation, room, player)
            || (!rebuilding && !RoomPurchases.CanAfford(simulation, room, player)))
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

        // The objects a room needs were bought with the room; corridor objects and a room's extra objects are bought
        // as they are placed.
        var extra = RoomPurchases.IsExtra(simulation, simulation.Map[Tile.X, Tile.Y].RoomId, ObjectType);
        if (extra && !RoomPurchases.CanAffordObject(simulation, ObjectType, player))
        {
            return;
        }

        ObjectPlacer.Place(simulation, simulation.Content.FindObject(ObjectType)!, Tile, Orientation);
        if (extra)
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
            var obj = (RoomObject)simulation.FindEntity(ObjectId)!;
            ObjectPlacer.PickUp(simulation, obj);

            // An extra object is sold back; placing it again buys it again.
            if (RoomPurchases.IsExtra(simulation, obj.RoomId, obj.ObjectType))
            {
                RoomPurchases.SellObject(simulation, obj.ObjectType, player);
            }
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
            if (!room.IsPaid && simulation.Content.FindRoom(room.RoomType) is { } definition)
            {
                RoomPurchases.PayForRoom(simulation, definition, player);
            }

            room.IsPaid = true;

            // CorsixTH's World:markRoomAsBuilt: building a diagnosis room puts its diagnosis in the casebook.
            if (DiseaseCatalog.Find("diag_" + room.RoomType) is { } diagnosis && player < simulation.Hospitals.Count)
            {
                simulation.Hospitals[player].CaseOf(diagnosis, simulation.Rules.Diseases).Discovered = true;
            }

            RoomVisits.OnRoomActivated(simulation, room);
            Tiredness.OnRoomActivated(simulation, room);
            RoomRebuild.OnRoomFinished(simulation, room, player);
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteInt32(RoomId);
}

/// <summary>
/// Starts editing a finished room (CorsixTH's <c>Room:tryToEdit</c>): it stops taking patients, sends its queue to
/// other rooms and its people out, and can be furnished again once empty.
/// </summary>
public sealed record EditRoomCommand(int RoomId) : Command
{
    public override CommandKind Kind => CommandKind.EditRoom;

    public override void Execute(Simulation simulation, int player)
    {
        if (simulation.FindEntity(RoomId) is Room { IsActive: true } room && RoomBuilder.IsOwnedBy(simulation.Map, room, player))
        {
            room.IsActive = false;
            room.IsClosing = true;
            RoomVisits.UpdateClosing(simulation, room);
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteInt32(RoomId);
}

/// <summary>Removes a room that is still being built, with its objects, door and walls; one being edited sells for half its cost.</summary>
public sealed record CancelRoomCommand(int RoomId) : Command
{
    public override CommandKind Kind => CommandKind.CancelRoom;

    public override void Execute(Simulation simulation, int player)
    {
        // A room wrecked by an explosion cannot be taken down this way (RemoveDestroyedRoomCommand).
        if (simulation.FindEntity(RoomId) is Room { IsActive: false, IsClosing: false, IsCrashed: false } room && RoomBuilder.IsOwnedBy(simulation.Map, room, player))
        {
            var definition = simulation.Content.FindRoom(room.RoomType);
            if (room.IsPaid)
            {
                // UIEditRoom:abortRoom for a room that was paid for: half its cost back, objects included.
                RoomRebuild.OnRoomFinished(simulation, room, player);
                RoomBuilder.Remove(simulation, room);
                if (definition is not null)
                {
                    RoomPurchases.RefundPaidRoom(simulation, definition, player);
                }

                return;
            }

            if (definition is not null)
            {
                RoomPurchases.SellExtraObjects(simulation, room, definition, player);
            }

            RoomBuilder.Remove(simulation, room);
            if (definition is not null)
            {
                RoomPurchases.SellNeededObjects(simulation, definition, player);
            }
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteInt32(RoomId);
}

/// <summary>
/// Takes a finished room being edited, once empty, back to its blueprint (CorsixTH's <c>returnToDoorPhase</c>, Esc while
/// furnishing it): see <see cref="RoomRebuild"/>.
/// </summary>
public sealed record ReturnToBlueprintCommand(int RoomId) : Command
{
    public override CommandKind Kind => CommandKind.ReturnToBlueprint;

    public override void Execute(Simulation simulation, int player)
    {
        if (simulation.FindEntity(RoomId) is Room room)
        {
            RoomRebuild.Start(simulation, room, player);
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteInt32(RoomId);
}

/// <summary>
/// Gives up a room taken back to its blueprint, for half its cost (CorsixTH's <c>abortRoom</c> after the
/// <c>delete_room</c> confirmation).
/// </summary>
public sealed record AbandonRebuildCommand : Command
{
    public override CommandKind Kind => CommandKind.AbandonRebuild;

    public override void Execute(Simulation simulation, int player) => RoomRebuild.Abandon(simulation, player);

    protected override void WriteArguments(SimWriter writer)
    {
    }
}

/// <summary>
/// Clears away a room wrecked by an explosion, for a fee (CorsixTH's <c>remove_destroyed_rooms</c> option: the confirmation
/// in <c>GameUI:onMouseUp</c>, <c>UIEditRoom:removeRoom</c> and <c>Room:calculateRemovalCost</c>): twice the room's cost,
/// less 40 % of each kind of object it needs as scrap. The hospital pays even into the red, as CorsixTH's
/// <c>spendMoney</c>.
/// </summary>
public sealed record RemoveDestroyedRoomCommand(int RoomId) : Command
{
    /// <summary>The share of a needed object's price recovered as scrap (<c>COST_RECOVERY</c>).</summary>
    private const decimal CostRecovery = 0.40m;

    public override CommandKind Kind => CommandKind.RemoveDestroyedRoom;

    /// <summary><c>Room:calculateRemovalCost</c>, or null for a room that is not a wreck of the player's.</summary>
    public static int? RemovalCost(Simulation simulation, Room room, int player)
    {
        if (!room.IsCrashed || !RoomBuilder.IsOwnedBy(simulation.Map, room, player) || player < 0 || player >= simulation.Hospitals.Count
            || simulation.Content.FindRoom(room.RoomType) is not { } definition)
        {
            return null;
        }

        var hospital = simulation.Hospitals[player];
        var cost = RoomPurchases.BuildCost(simulation, hospital, definition) * 2;
        foreach (var (objectId, _) in LevelRules.Needed(definition))
        {
            cost -= (int)decimal.Floor(hospital.Research.ObjectCost(simulation.Rules, objectId) * CostRecovery);
        }

        return cost;
    }

    public override void Execute(Simulation simulation, int player)
    {
        if (simulation.FindEntity(RoomId) is Room room && RemovalCost(simulation, room, player) is { } cost)
        {
            RoomBuilder.Remove(simulation, room);
            simulation.Hospitals[player].Spend(simulation.Hours, cost, TransactionKind.RemoveRoom, room.RoomType);
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

/// <summary>
/// Picks up a member of the issuing player's staff and puts them down on a tile (CorsixTH's pickup action and
/// <c>UIPlaceStaff</c>), where they start afresh; ignored where <see cref="StaffMoving.Check"/> says no.
/// </summary>
public sealed record MoveStaffCommand(int StaffId, TilePosition Tile) : Command
{
    public override CommandKind Kind => CommandKind.MoveStaff;

    public override void Execute(Simulation simulation, int player)
    {
        if (StaffMoving.Check(simulation, StaffId, Tile, player) == StaffHireProblem.None && simulation.FindEntity(StaffId) is Humanoid staff)
        {
            StaffMoving.Move(simulation, staff, Tile);
        }
    }

    protected override void WriteArguments(SimWriter writer)
    {
        writer.WriteInt32(StaffId);
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

/// <summary>
/// The player borrows 5,000 from the bank, or with <paramref name="AsMuchAsAllowed"/> up to the limit, as the bank
/// manager's button does with Ctrl (CorsixTH's <c>UIBankManager:increaseLoan</c>).
/// </summary>
public sealed record BorrowCommand(bool AsMuchAsAllowed) : Command
{
    public override CommandKind Kind => CommandKind.Borrow;

    public override void Execute(Simulation simulation, int player)
    {
        if (player >= 0 && player < simulation.Hospitals.Count)
        {
            simulation.Hospitals[player].Borrow(simulation.Hours, AsMuchAsAllowed);
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteBool(AsMuchAsAllowed);
}

/// <summary>
/// The player pays back 5,000, or with <paramref name="AsMuchAsPossible"/> as much as the bank balance allows in
/// steps of 5,000 (CorsixTH's <c>UIBankManager:decreaseLoan</c>).
/// </summary>
public sealed record RepayCommand(bool AsMuchAsPossible) : Command
{
    public override CommandKind Kind => CommandKind.Repay;

    public override void Execute(Simulation simulation, int player)
    {
        if (player >= 0 && player < simulation.Hospitals.Count)
        {
            simulation.Hospitals[player].Repay(simulation.Hours, AsMuchAsPossible);
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteBool(AsMuchAsPossible);
}

/// <summary>The player buys a parcel of land, as clicking it on CorsixTH's town map does (<c>UITownMap</c>).</summary>
public sealed record PurchaseParcelCommand(int Parcel) : Command
{
    public override CommandKind Kind => CommandKind.PurchaseParcel;

    public override void Execute(Simulation simulation, int player) => simulation.PurchaseParcel(Parcel, player);

    protected override void WriteArguments(SimWriter writer) => writer.WriteInt32(Parcel);
}

/// <summary>
/// Sets what the hospital charges for a disease or a diagnosis in its casebook, as a percentage of the standard price
/// (CorsixTH's <c>UICasebook:increasePay</c> and <c>decreasePay</c>): held to 50–200, and only for one the casebook
/// shows, that is one the hospital knows.
/// </summary>
public sealed record SetTreatmentPriceCommand(string DiseaseId, int PricePercent) : Command
{
    public override CommandKind Kind => CommandKind.SetTreatmentPrice;

    public override void Execute(Simulation simulation, int player)
    {
        if (player < simulation.Hospitals.Count && DiseaseCatalog.Find(DiseaseId) is { } disease)
        {
            simulation.Hospitals[player].SetTreatmentPrice(disease, simulation.Rules.Diseases, PricePercent);
        }
    }

    protected override void WriteArguments(SimWriter writer)
    {
        writer.WriteString(DiseaseId);
        writer.WriteInt32(PricePercent);
    }
}

/// <summary>
/// Sets how hot the hospital's radiators are, in tenths from 1 to 10, as the town map's heating buttons do; not while
/// the boiler is broken.
/// </summary>
public sealed record SetRadiatorHeatCommand(int Tenths) : Command
{
    public override CommandKind Kind => CommandKind.SetRadiatorHeat;

    public override void Execute(Simulation simulation, int player)
    {
        if (player < simulation.Hospitals.Count)
        {
            simulation.Hospitals[player].SetRadiatorHeat(Tenths);
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteInt32(Tenths);
}

/// <summary>
/// Sets one of the issuing player's hospital policies, as moving a slider or a switch in CorsixTH's policy window
/// (<c>UIPolicy</c>); sliders are in thousandths.
/// </summary>
public sealed record SetPolicyCommand(PolicyKind Policy, int Value) : Command
{
    public override CommandKind Kind => CommandKind.SetPolicy;

    public override void Execute(Simulation simulation, int player)
    {
        if (player < simulation.Hospitals.Count)
        {
            simulation.Hospitals[player].Policies.Set(Policy, Value);
        }
    }

    protected override void WriteArguments(SimWriter writer)
    {
        writer.WriteByte((byte)Policy);
        writer.WriteInt32(Value);
    }
}

/// <summary>
/// Moves an area's share of the research points up or down by <paramref name="Change"/> percent (the research window's
/// arrows, <c>UIResearch:adjustResearch</c>).
/// </summary>
public sealed record AdjustResearchCommand(Research.ResearchCategory Category, int Change) : Command
{
    public override CommandKind Kind => CommandKind.AdjustResearch;

    public override void Execute(Simulation simulation, int player)
    {
        if (player < simulation.Hospitals.Count)
        {
            simulation.Hospitals[player].Research.AdjustShare(Category, Change);
        }
    }

    protected override void WriteArguments(SimWriter writer)
    {
        writer.WriteByte((byte)Category);
        writer.WriteInt32(Change);
    }
}

/// <summary>Concentrates research on a disease, or stops if it already does (the casebook's concentrate button).</summary>
public sealed record ConcentrateResearchCommand(string DiseaseId) : Command
{
    public override CommandKind Kind => CommandKind.ConcentrateResearch;

    public override void Execute(Simulation simulation, int player)
    {
        if (player < simulation.Hospitals.Count && DiseaseCatalog.Find(DiseaseId) is { } disease)
        {
            var hospital = simulation.Hospitals[player];
            if (string.Equals(hospital.Research.Concentrated, DiseaseId, StringComparison.Ordinal)
                || (hospital.CaseOf(disease, simulation.Rules.Diseases).Discovered && hospital.Research.CanConcentrate(simulation, hospital, disease)))
            {
                hospital.Research.Concentrate(simulation, hospital, disease);
            }
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteString(DiseaseId);
}

/// <summary>Fires one of the issuing player's staff (CorsixTH's staff window and staff management screen).</summary>
public sealed record FireStaffCommand(int StaffId) : Command
{
    public override CommandKind Kind => CommandKind.FireStaff;

    public override void Execute(Simulation simulation, int player)
    {
        if (simulation.FindEntity(StaffId) is Humanoid { Profile: not null, IsFired: false } staff && staff.Employer == player)
        {
            StaffEmployment.Fire(simulation, staff);
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteInt32(StaffId);
}

/// <summary>Pays one of the issuing player's staff a bonus of a tenth of their wage (the staff management screen).</summary>
public sealed record PayBonusCommand(int StaffId) : Command
{
    public override CommandKind Kind => CommandKind.PayBonus;

    public override void Execute(Simulation simulation, int player)
    {
        if (simulation.FindEntity(StaffId) is Humanoid { Profile: not null, IsFired: false } staff && staff.Employer == player)
        {
            StaffEmployment.PayBonus(simulation, staff);
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteInt32(StaffId);
}

/// <summary>Raises one of the issuing player's staff's wage by a tenth (the staff management screen).</summary>
public sealed record IncreaseSalaryCommand(int StaffId) : Command
{
    public override CommandKind Kind => CommandKind.IncreaseSalary;

    public override void Execute(Simulation simulation, int player)
    {
        if (simulation.FindEntity(StaffId) is Humanoid { Profile: not null, IsFired: false } staff && staff.Employer == player)
        {
            StaffEmployment.IncreaseSalary(simulation, staff);
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteInt32(StaffId);
}

/// <summary>
/// Answers a member of staff's request for a raise (CorsixTH's <c>UIStaffRise</c>): granting it raises their wage,
/// refusing it fires them.
/// </summary>
public sealed record AnswerRaiseCommand(int StaffId, bool Grant) : Command
{
    public override CommandKind Kind => CommandKind.AnswerRaise;

    public override void Execute(Simulation simulation, int player)
    {
        if (simulation.FindEntity(StaffId) is Humanoid { Profile: not null, IsFired: false } staff && staff.Employer == player)
        {
            StaffEmployment.AnswerRaise(simulation, staff, Grant);
        }
    }

    protected override void WriteArguments(SimWriter writer)
    {
        writer.WriteInt32(StaffId);
        writer.WriteBool(Grant);
    }
}

/// <summary>
/// Answers the fax in which a VIP asks to visit the issuing player's hospital (CorsixTH's <c>accept_vip</c> and
/// <c>refuse_vip</c> choices); a VIP refused too often may come anyway.
/// </summary>
public sealed record AnswerVipCommand(bool Accept) : Command
{
    public override CommandKind Kind => CommandKind.AnswerVip;

    public override void Execute(Simulation simulation, int player) => VipVisits.Answer(simulation, player, Accept);

    protected override void WriteArguments(SimWriter writer) => writer.WriteBool(Accept);
}

/// <summary>
/// Answers the fax offering the issuing player's hospital an emergency (CorsixTH's <c>accept_emergency</c> and
/// <c>refuse_emergency</c> choices).
/// </summary>
public sealed record AnswerEmergencyCommand(bool Accept) : Command
{
    public override CommandKind Kind => CommandKind.AnswerEmergency;

    public override void Execute(Simulation simulation, int player) => Disasters.Emergencies.Answer(simulation, player, Accept);

    protected override void WriteArguments(SimWriter writer) => writer.WriteBool(Accept);
}

/// <summary>
/// Answers the fax telling the issuing player's hospital of an epidemic (CorsixTH's <c>cover_up_epidemic</c> and
/// <c>declare_epidemic</c> choices).
/// </summary>
public sealed record AnswerEpidemicCommand(bool CoverUp) : Command
{
    public override CommandKind Kind => CommandKind.AnswerEpidemic;

    public override void Execute(Simulation simulation, int player) => Disasters.Epidemics.Answer(simulation, player, CoverUp);

    protected override void WriteArguments(SimWriter writer) => writer.WriteBool(CoverUp);
}

/// <summary>
/// Picks an infected patient of the issuing player's hospital to be vaccinated during an epidemic's cover-up (CorsixTH's
/// click on a patient); a free nurse nearby comes when the patient stands still.
/// </summary>
public sealed record VaccinateCommand(int PatientId) : Command
{
    public override CommandKind Kind => CommandKind.Vaccinate;

    public override void Execute(Simulation simulation, int player) => Disasters.Epidemics.MarkForVaccination(simulation, player, PatientId);

    protected override void WriteArguments(SimWriter writer) => writer.WriteInt32(PatientId);
}

/// <summary>
/// Answers the fax a waiting patient of the issuing player's hospital sent (CorsixTH's <c>send_home</c>, <c>wait</c>
/// and <c>guess_cure</c> choices).
/// </summary>
public sealed record AnswerPatientCommand(int PatientId, PatientAnswer Answer) : Command
{
    public override CommandKind Kind => CommandKind.AnswerPatient;

    public override void Execute(Simulation simulation, int player)
    {
        if (simulation.FindEntity(PatientId) is Humanoid { Behaviour: PatientBehaviour patient, IsDespawning: false } humanoid && humanoid.HospitalPlayer == player)
        {
            patient.Answer(simulation, humanoid, Answer);
        }
    }

    protected override void WriteArguments(SimWriter writer)
    {
        writer.WriteInt32(PatientId);
        writer.WriteByte((byte)Answer);
    }
}

/// <summary>
/// Sends a patient of the issuing player's hospital home from the patient window (CorsixTH's <c>UIPatient:goHome</c>),
/// counted as kicked out.
/// </summary>
public sealed record SendPatientHomeCommand(int PatientId) : Command
{
    public override CommandKind Kind => CommandKind.SendPatientHome;

    public override void Execute(Simulation simulation, int player)
    {
        if (simulation.FindEntity(PatientId) is Humanoid { Behaviour: PatientBehaviour patient, IsDespawning: false } humanoid && humanoid.HospitalPlayer == player)
        {
            patient.SendHome(simulation, humanoid);
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteInt32(PatientId);
}

/// <summary>
/// Guesses the cure of a patient of the issuing player's hospital from the patient window (CorsixTH's
/// <c>UIPatient:guessDisease</c>): diagnosed at once, they go for treatment if they agree to pay.
/// </summary>
public sealed record GuessPatientCureCommand(int PatientId) : Command
{
    public override CommandKind Kind => CommandKind.GuessPatientCure;

    public override void Execute(Simulation simulation, int player)
    {
        if (simulation.FindEntity(PatientId) is Humanoid { Behaviour: PatientBehaviour patient, IsDespawning: false } humanoid && humanoid.HospitalPlayer == player)
        {
            patient.GuessCure(simulation, humanoid);
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteInt32(PatientId);
}

/// <summary>
/// Makes the queue of a room's door or of a reception desk of the issuing player longer or shorter by
/// <paramref name="Amount"/> (CorsixTH's queue window, <c>UIQueue:increaseMaxSize</c> and <c>decreaseMaxSize</c>: 1, 5
/// with Shift or 10 with Ctrl).
/// </summary>
/// <param name="OwnerId">The room, or the reception desk object.</param>
/// <param name="Amount">People to add to the queue's size, or to take off when negative.</param>
public sealed record ChangeQueueSizeCommand(int OwnerId, int Amount) : Command
{
    public override CommandKind Kind => CommandKind.ChangeQueueSize;

    public override void Execute(Simulation simulation, int player)
    {
        var queue = simulation.FindEntity(OwnerId) switch
        {
            Room room when RoomBuilder.IsOwnedBy(simulation.Map, room, player) => room.DoorQueue,
            RoomObject { Desk: { } desk } placed when simulation.Map.GetParcelOwner(simulation.Map[placed.Tile.X, placed.Tile.Y].Parcel) == player + 1 => desk.Queue,
            _ => null,
        };
        queue?.ChangeMaxSize(Amount);
    }

    protected override void WriteArguments(SimWriter writer)
    {
        writer.WriteInt32(OwnerId);
        writer.WriteInt32(Amount);
    }
}

/// <summary>
/// Moves a patient of the issuing player's hospital to another place in the queue they stand in (CorsixTH's queue
/// window, <c>Queue:movePatient</c>): 0 is the front.
/// </summary>
public sealed record MoveInQueueCommand(int PatientId, int Position) : Command
{
    public override CommandKind Kind => CommandKind.MoveInQueue;

    public override void Execute(Simulation simulation, int player)
    {
        if (simulation.FindEntity(PatientId) is Humanoid { Behaviour: PatientBehaviour patient, IsDespawning: false } humanoid
            && humanoid.HospitalPlayer == player
            && patient.CurrentQueue(simulation) is { } queue)
        {
            queue.Move(simulation, queue.IndexOf(humanoid.Id), Position);
        }
    }

    protected override void WriteArguments(SimWriter writer)
    {
        writer.WriteInt32(PatientId);
        writer.WriteInt32(Position);
    }
}

/// <summary>
/// Sends a patient of the issuing player's hospital, on their way to a room or queueing at its door, to another active room
/// of the same type of that hospital instead (CorsixTH's queue window, a patient dropped on a room in
/// <c>UIQueue:onMouseUp</c>).
/// </summary>
public sealed record SendPatientToRoomCommand(int PatientId, int RoomId) : Command
{
    public override CommandKind Kind => CommandKind.SendPatientToRoom;

    public override void Execute(Simulation simulation, int player)
    {
        if (simulation.FindEntity(PatientId) is Humanoid { Behaviour: PatientBehaviour patient, IsDespawning: false } humanoid
            && humanoid.HospitalPlayer == player
            && simulation.FindEntity(RoomId) is Room room
            && RoomBuilder.IsOwnedBy(simulation.Map, room, player))
        {
            patient.SendToRoom(simulation, humanoid, room);
        }
    }

    protected override void WriteArguments(SimWriter writer)
    {
        writer.WriteInt32(PatientId);
        writer.WriteInt32(RoomId);
    }
}

/// <summary>
/// Keeps a handyman of the issuing player to one of the player's parcels, or to all of them with 0 (CorsixTH's staff
/// window, <c>UIStaff:changeParcel</c>, which steps through the hospital's parcels and back to all). Ignored for a parcel
/// the player does not own.
/// </summary>
public sealed record SetHandymanParcelCommand(int HandymanId, int Parcel) : Command
{
    public override CommandKind Kind => CommandKind.SetHandymanParcel;

    public override void Execute(Simulation simulation, int player)
    {
        if (simulation.FindEntity(HandymanId) is Humanoid { Behaviour: HandymanBehaviour handyman, IsDespawning: false } humanoid
            && humanoid.Employer == player
            && (Parcel == 0 || (Parcel > 0 && Parcel < simulation.Map.ParcelCount && simulation.Map.GetParcelOwner(Parcel) == player + 1)))
        {
            handyman.Parcel = Parcel;
        }
    }

    protected override void WriteArguments(SimWriter writer)
    {
        writer.WriteInt32(HandymanId);
        writer.WriteInt32(Parcel);
    }
}

/// <summary>
/// Gives a handyman of the issuing player more of one kind of work (the staff window's priority buttons,
/// <c>UIStaff:doMoreCleaning</c>, <c>doMoreWatering</c> and <c>doMoreRepairing</c>); the hospital remembers that the
/// priorities have been changed (<c>handyman_popup</c>).
/// </summary>
public sealed record IncreaseHandymanPriorityCommand(int HandymanId, HandymanTaskType Task) : Command
{
    public override CommandKind Kind => CommandKind.IncreaseHandymanPriority;

    public override void Execute(Simulation simulation, int player)
    {
        if (simulation.FindEntity(HandymanId) is Humanoid { Behaviour: HandymanBehaviour handyman, IsDespawning: false } humanoid
            && humanoid.Employer == player
            && Enum.IsDefined(Task))
        {
            handyman.IncreasePriority(Task);
            simulation.Hospitals[player].HandymanPrioritiesChanged = true;
        }
    }

    protected override void WriteArguments(SimWriter writer)
    {
        writer.WriteInt32(HandymanId);
        writer.WriteByte((byte)Task);
    }
}

/// <summary>
/// Calls a handyman to a machine of the issuing player's hospital (the machine window's call button,
/// <c>UIMachine:callHandyman</c>): its repair becomes urgent and its room lets no patient in until then.
/// </summary>
public sealed record CallHandymanCommand(int MachineId) : Command
{
    public override CommandKind Kind => CommandKind.CallHandyman;

    public override void Execute(Simulation simulation, int player)
    {
        if (OwnMachine(simulation, MachineId, player) is { } machine)
        {
            Machines.CallHandyman(machine);
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteInt32(MachineId);

    /// <summary>A machine in a room of the player's that has not crashed, or null.</summary>
    internal static RoomObject? OwnMachine(Simulation simulation, int machineId, int player) =>
        simulation.FindEntity(machineId) is RoomObject { Machine: not null, IsDestroyed: false } machine
        && simulation.FindEntity(machine.RoomId) is Room { IsCrashed: false } room && RoomVisits.Owner(simulation, room) == player
            ? machine
            : null;
}

/// <summary>
/// Replaces a machine of the issuing player's hospital with a new one at today's price (the machine window's replace
/// button, <c>Machine:replaceMachine</c>). Ignored without the money.
/// </summary>
public sealed record ReplaceMachineCommand(int MachineId) : Command
{
    public override CommandKind Kind => CommandKind.ReplaceMachine;

    public override void Execute(Simulation simulation, int player)
    {
        if (CallHandymanCommand.OwnMachine(simulation, MachineId, player) is { } machine && player >= 0 && player < simulation.Hospitals.Count)
        {
            Machines.Replace(simulation, simulation.Hospitals[player], machine);
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteInt32(MachineId);
}

/// <summary>
/// Shoots a rat of the issuing player's hospital (CorsixTH's click on a rat, <c>Rat:onClick</c>); a dead rat is left on
/// the floor.
/// </summary>
public sealed record ShootRatCommand(int RatId) : Command
{
    public override CommandKind Kind => CommandKind.ShootRat;

    public override void Execute(Simulation simulation, int player)
    {
        if (simulation.FindEntity(RatId) is Rat { IsGone: false } rat && rat.Player == player)
        {
            Disasters.Rats.Shoot(simulation, player, rat);
        }
    }

    protected override void WriteArguments(SimWriter writer) => writer.WriteInt32(RatId);
}

/// <summary>Performs one of CorsixTH's menu cheats for the issuing player's hospital (<see cref="Cheats"/>).</summary>
public sealed record CheatCommand(CheatKind Cheat) : Command
{
    public override CommandKind Kind => CommandKind.Cheat;

    public override void Execute(Simulation simulation, int player) => Cheats.Perform(simulation, player, Cheat);

    protected override void WriteArguments(SimWriter writer) => writer.WriteByte((byte)Cheat);
}
