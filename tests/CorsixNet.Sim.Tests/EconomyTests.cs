using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;
using static CorsixNet.Sim.Definitions.FootprintFlags;

namespace CorsixNet.Sim.Tests;

/// <summary>Paying for rooms and their objects, and selling them back.</summary>
public class EconomyTests
{
    private static readonly ObjectDefinition Cabinet = new("cabinet", "Cabinet", 2, new Dictionary<Direction, ObjectOrientation>
    {
        [Direction.North] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition)]),
    });

    private static readonly RoomDefinition Office = new(
        "office", "Office", 4, 18, new WallSet(82, 83, 90, 91), new Dictionary<string, int> { ["cabinet"] = 2 }, LevelConfigId: 7);

    private static readonly GameContent Content = new([Office], [114], [Cabinet]);

    private static readonly LevelRules Rules = new(
        3000,
        [KeyValuePair.Create("office", 1000)],
        [KeyValuePair.Create("cabinet", 300)]);

    private static readonly RoomPlan Plan = new("office", new TileRect(2, 2, 4, 4), new DoorPlacement(new(3, 6), EdgeOrientation.North));

    [Fact]
    public void ARoomCostsItsOwnPricePlusItsObjects()
    {
        Assert.Equal(1000, Rules.RoomCost("office"));
        Assert.Equal(300, Rules.ObjectCost("cabinet"));
        Assert.Equal(0, Rules.ObjectCost("unknown"));
        Assert.Equal(600, Rules.NeededObjectsCost(Office));
        Assert.Equal(1600, Rules.BuildCost(Office));
        Assert.Throws<ArgumentException>(() => new LevelRules(0, [KeyValuePair.Create("a", 1), KeyValuePair.Create("a", 2)], []));
    }

    [Fact]
    public void ObjectsArePaidForWhenTheRoomIsBuiltAndTheRoomWhenItIsConfirmed()
    {
        var simulation = NewSimulation(Rules);
        var hospital = simulation.Hospitals[0];
        Assert.Equal(3000, hospital.Balance);

        Run(simulation, new BuildRoomCommand(Plan));
        var room = Assert.Single(simulation.Entities.OfType<Room>());
        Assert.Equal(2400, hospital.Balance);
        Assert.Equal(new Transaction(0, TransactionKind.BuyObject, "cabinet", -600, 2400), hospital.Transactions[0]);

        // Placing and picking up objects costs nothing: they are paid for already.
        Run(simulation, new PlaceObjectCommand("cabinet", new(2, 2), Direction.North));
        Run(simulation, new PickUpObjectCommand(simulation.Entities.OfType<RoomObject>().Single().Id));
        Run(simulation, new PlaceObjectCommand("cabinet", new(2, 2), Direction.North));
        Run(simulation, new PlaceObjectCommand("cabinet", new(4, 2), Direction.North));
        Assert.Equal(2400, hospital.Balance);

        Run(simulation, new ConfirmRoomCommand(room.Id));
        Assert.True(room.IsActive);
        Assert.Equal(1400, hospital.Balance);
        Assert.Equal(TransactionKind.BuildRoom, hospital.Transactions[0].Kind);
        Assert.Equal("office", hospital.Transactions[0].Subject);
        Assert.Equal(-1000, hospital.Transactions[0].Amount);
        Assert.Equal(2, hospital.Transactions.Count);
    }

    [Theory]
    [InlineData(1599, false)]
    [InlineData(1600, true)]
    public void ARoomCanOnlyBeStartedWithItsWholeCostInTheBank(int startCash, bool built)
    {
        var simulation = NewSimulation(new LevelRules(startCash, [KeyValuePair.Create("office", 1000)], [KeyValuePair.Create("cabinet", 300)]));

        Assert.Equal(built, RoomPurchases.CanAfford(simulation, Office, 0));
        Run(simulation, new BuildRoomCommand(Plan));

        Assert.Equal(built, simulation.Entities.OfType<Room>().Any());
        Assert.Equal(built ? startCash - 600 : startCash, simulation.Hospitals[0].Balance);
    }

    [Fact]
    public void TakingDownAnUnfinishedRoomSellsItsObjectsBack()
    {
        var simulation = NewSimulation(Rules);
        Run(simulation, new BuildRoomCommand(Plan));
        Run(simulation, new PlaceObjectCommand("cabinet", new(2, 2), Direction.North));
        var room = simulation.Entities.OfType<Room>().Single();

        Run(simulation, new CancelRoomCommand(room.Id));

        var hospital = simulation.Hospitals[0];
        Assert.Equal(3000, hospital.Balance);
        Assert.Equal(new Transaction(2, TransactionKind.SellObject, "cabinet", 300, 3000), hospital.Transactions[0]);
        Assert.Equal(new Transaction(2, TransactionKind.SellObject, "cabinet", 300, 2700), hospital.Transactions[1]);
    }

    [Fact]
    public void AnObjectBeyondWhatTheRoomNeedsIsBoughtWhenPlacedAndSoldWhenPickedUp()
    {
        var simulation = NewSimulation(Rules);
        var hospital = simulation.Hospitals[0];
        Run(simulation, new BuildRoomCommand(Plan));
        Run(simulation, new PlaceObjectCommand("cabinet", new(2, 2), Direction.North));
        Run(simulation, new PlaceObjectCommand("cabinet", new(4, 2), Direction.North));
        Assert.Equal(2400, hospital.Balance);

        // A third cabinet is an extra (UIPlaceObjects:addObjects buys it), picking it up sells it back.
        Run(simulation, new PlaceObjectCommand("cabinet", new(5, 2), Direction.North));
        Assert.Equal(2100, hospital.Balance);
        Assert.Equal(new Transaction(3, TransactionKind.BuyObject, "cabinet", -300, 2100), hospital.Transactions[0]);
        var third = simulation.Entities.OfType<RoomObject>().Single(o => o.Tile == new TilePosition(5, 2));
        Run(simulation, new PickUpObjectCommand(third.Id));
        Assert.Equal(2400, hospital.Balance);
        Assert.Equal(TransactionKind.SellObject, hospital.Transactions[0].Kind);

        // Picking up one of the two needed cabinets is free, as before.
        Run(simulation, new PickUpObjectCommand(simulation.Entities.OfType<RoomObject>().First().Id));
        Assert.Equal(2400, hospital.Balance);
    }

    [Fact]
    public void TakingDownAnUnfinishedRoomSellsItsExtraObjectsBackToo()
    {
        var simulation = NewSimulation(Rules);
        Run(simulation, new BuildRoomCommand(Plan));
        foreach (var x in new[] { 2, 4, 5 })
        {
            Run(simulation, new PlaceObjectCommand("cabinet", new(x, 2), Direction.North));
        }

        Assert.Equal(2100, simulation.Hospitals[0].Balance);
        Run(simulation, new CancelRoomCommand(simulation.Entities.OfType<Room>().Single().Id));

        Assert.Equal(3000, simulation.Hospitals[0].Balance);
    }

    [Fact]
    public void TheLogKeepsTheLatestTransactions()
    {
        var simulation = NewSimulation(Rules);
        for (var i = 0; i < 8; i++)
        {
            Run(simulation, new BuildRoomCommand(Plan));
            Run(simulation, new CancelRoomCommand(simulation.Entities.OfType<Room>().Single().Id));
        }

        var log = simulation.Hospitals[0].Transactions;
        Assert.Equal(Hospital.LogLength, log.Count);
        Assert.Equal(TransactionKind.SellObject, log[0].Kind);
        Assert.Equal(15, log[0].Hour);
        Assert.Equal(3000, log[0].Balance);
    }

    [Fact]
    public void SaveAndLoadKeepMoneyAndPrices()
    {
        var simulation = NewSimulation(Rules);
        Run(simulation, new BuildRoomCommand(Plan));

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, Content);

        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        Assert.Equal(2400, loaded.Hospitals[0].Balance);
        Assert.Equal(simulation.Hospitals[0].Transactions, loaded.Hospitals[0].Transactions);
        Assert.Equal(1600, loaded.Rules.BuildCost(Office));
        Assert.Equal(3000, loaded.Rules.StartCash);
    }

    [Fact]
    public void WithoutRulesEverythingIsFree()
    {
        var simulation = NewSimulation(rules: null);

        Run(simulation, new BuildRoomCommand(Plan));

        Assert.Single(simulation.Entities.OfType<Room>());
        Assert.Equal(0, simulation.Hospitals[0].Balance);
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, false, false)]
    public void OnlyRoomsWhoseObjectsAreThereFromTheStartCanBeBuilt(bool forLevel, bool atStart, bool available, bool built)
    {
        var rules = new LevelRules(3000, [], [], objectAvailability: [KeyValuePair.Create("cabinet", new ObjectAvailability(forLevel, atStart))]);
        Assert.Equal(available, rules.IsRoomAvailable(Office));
        Assert.Equal(built, rules.IsRoomDiscoveredAtStart(Office));
        var simulation = NewSimulation(rules);
        Assert.Equal(built, simulation.Hospitals[0].IsRoomDiscovered("office"));
        Assert.Equal(built, RoomPurchases.CanBuild(simulation, Office, 0));

        Run(simulation, new BuildRoomCommand(Plan));

        Assert.Equal(built, simulation.Entities.OfType<Room>().Any());

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream, Content);
        Assert.Equal(simulation.ComputeStateHash(), loaded.ComputeStateHash());
        Assert.Equal(built, loaded.Hospitals[0].IsRoomDiscovered("office"));
        Assert.Equal(available, loaded.Rules.IsRoomAvailable(Office));
    }

    [Fact]
    public void AnObjectTheLevelDoesNotNameIsThereFromTheStart()
    {
        Assert.Equal(new ObjectAvailability(true, true), Rules.Availability("cabinet"));
        Assert.True(Rules.IsRoomDiscoveredAtStart(Office));
        Assert.Equal(["office"], NewSimulation(Rules).Hospitals[0].DiscoveredRooms);
    }

    private static void Run(Simulation simulation, Command command, int player = 0)
    {
        simulation.Schedule(player, command);
        simulation.Step();
    }

    private static Simulation NewSimulation(LevelRules? rules)
    {
        var map = TestMaps.FromRows(
            "............",
            "............",
            "............",
            "............",
            "............",
            "............",
            "............",
            "............",
            "............",
            "............");
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Parcel = 1;
                map[x, y].Set(TileFlags.Buildable | TileFlags.Hospital | TileFlags.BuildableNorth | TileFlags.BuildableEast | TileFlags.BuildableSouth | TileFlags.BuildableWest, true);
            }
        }

        map.UpdatePathfinding();
        map.UpdateShadows();
        return new Simulation(map, seed: 7, Content, rules);
    }
}
