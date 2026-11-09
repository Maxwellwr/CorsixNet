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
            CommandKind.SetTreatmentPrice => new SetTreatmentPriceCommand(reader.ReadString(), reader.ReadInt32()),
            _ => throw new InvalidDataException($"Unknown command kind {(ushort)kind}."),
        };
    }

    protected abstract void WriteArguments(SimWriter writer);

    private static CheatKind ReadCheat(SimReader reader)
    {
        var cheat = reader.ReadByte();
        return cheat is >= (byte)CheatKind.Money and <= (byte)CheatKind.ToggleSuperDoctor ? (CheatKind)cheat : throw new InvalidDataException($"Unknown cheat {cheat}.");
    }

    private static PatientAnswer ReadPatientAnswer(SimReader reader)
    {
        var answer = reader.ReadByte();
        return answer is >= (byte)PatientAnswer.SendHome and <= (byte)PatientAnswer.GuessCure ? (PatientAnswer)answer : throw new InvalidDataException($"Unknown patient answer {answer}.");
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
        if (RoomBuilder.Check(simulation.Map, simulation.Content, Plan, player) != RoomPlanProblem.None
            || simulation.Content.FindRoom(Plan.RoomType) is not { } room
            || !RoomPurchases.CanBuild(simulation, room, player)
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

            // CorsixTH's World:markRoomAsBuilt: building a diagnosis room puts its diagnosis in the casebook.
            if (DiseaseCatalog.Find("diag_" + room.RoomType) is { } diagnosis && player < simulation.Hospitals.Count)
            {
                simulation.Hospitals[player].CaseOf(diagnosis, simulation.Rules.Diseases).Discovered = true;
            }

            RoomVisits.OnRoomActivated(simulation, room);
            Tiredness.OnRoomActivated(simulation, room);
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
