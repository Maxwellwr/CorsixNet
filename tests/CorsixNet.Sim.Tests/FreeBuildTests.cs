using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;
using static CorsixNet.Sim.Definitions.FootprintFlags;

namespace CorsixNet.Sim.Tests;

/// <summary>Free build mode: a level played without money (CorsixTH's <c>free_build_mode</c>).</summary>
public class FreeBuildTests
{
    private static readonly ObjectDefinition Cabinet = new("cabinet", "Cabinet", 2, new Dictionary<Direction, ObjectOrientation>
    {
        [Direction.North] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition)]),
    });

    private static readonly RoomDefinition Office = new(
        "office", "Office", 4, 18, new WallSet(82, 83, 90, 91), new Dictionary<string, int> { ["cabinet"] = 2 }, LevelConfigId: 7);

    private static readonly GameContent Content = new([Office], [114], [Cabinet]);

    private static readonly LevelRules Level = new(
        3000,
        [KeyValuePair.Create("office", 1000)],
        [KeyValuePair.Create("cabinet", 300)],
        landCostPerTile: 30,
        endConditions: new EndConditions([new(EndCriterion.Balance, 1, AtLeast: true, 1000, 0, 3000)], []));

    [Fact]
    public void InFreeBuildModeTheLevelHasNoMoneyCostsOrGoals()
    {
        var free = Level.WithFreeBuild();

        Assert.True(free.FreeBuild);
        Assert.Equal(0, free.StartCash);
        Assert.Equal(0, free.RoomCost("office"));
        Assert.Equal(0, free.ObjectCost("cabinet"));
        Assert.Equal(0, free.BuildCost(Office));
        Assert.Empty(free.EndConditions.WinGoals);
        Assert.Equal(StaffRules.Default.Levels, free.Staff.Levels);
        Assert.All(StaffClasses.HireOrder, staffClass => Assert.Equal(0, free.Staff.MinSalary(staffClass)));

        // The level itself is unchanged.
        Assert.False(Level.FreeBuild);
        Assert.Equal(3000, Level.StartCash);
        Assert.Equal(1600, Level.BuildCost(Office));
        Assert.Single(Level.EndConditions.WinGoals);
    }

    [Fact]
    public void BuildingAndLandCostNothingAndNoMoneyChangesHands()
    {
        var simulation = NewSimulation(Level.WithFreeBuild());
        var hospital = simulation.Hospitals[0];
        var value = hospital.Value;
        Assert.Equal(0, hospital.Balance);
        Assert.Equal(0, simulation.ParcelPrice(2));

        Run(simulation, new BuildRoomCommand(new RoomPlan("office", new TileRect(2, 2, 4, 4), new DoorPlacement(new(3, 6), EdgeOrientation.North))));
        Assert.Single(simulation.Entities.OfType<Room>());
        Run(simulation, new PurchaseParcelCommand(2));
        Assert.Equal(1, simulation.Map.GetParcelOwner(2));

        // Hospital:spendMoney and receiveMoney do nothing at all, not even log or change the value.
        hospital.Spend(1, 500, TransactionKind.Wages, string.Empty);
        hospital.Receive(1, 500, TransactionKind.Cheat, string.Empty);
        Assert.Equal(0, hospital.Balance);
        Assert.Equal(value, hospital.Value);
        Assert.Empty(hospital.Transactions);
        Assert.Equal(0, hospital.MoneyInThisMonth + hospital.MoneyOutThisMonth);

        // The bank lends nothing.
        Assert.Equal(0, hospital.MaxLoan);
        hospital.Borrow(1, asMuchAsAllowed: true);
        Assert.Equal(0, hospital.Loan);
    }

    [Fact]
    public void StaffWorkForNothingAndNeverAskForARaise()
    {
        var simulation = NewSimulation(Level.WithFreeBuild());
        Assert.All(simulation.AvailableStaff(StaffClass.Doctor), profile => Assert.Equal(0, profile.Wage));

        Run(simulation, new HireStaffCommand(StaffClass.Doctor, 0, new TilePosition(8, 8)));
        var doctor = simulation.Entities.OfType<Humanoid>().Last();
        Assert.Equal(0, doctor.Profile!.Wage);

        doctor.Happiness = 0.05m;
        for (var hour = 0; hour < 5000; hour++)
        {
            StaffEmployment.Tick(simulation, doctor);
        }

        Assert.Equal(0, doctor.RaiseRequest);
    }

    [Fact]
    public void EveryVipVisitBringsTwentyReputationAndNoMoney()
    {
        var simulation = NewSimulation(Level.WithFreeBuild());
        var hospital = simulation.Hospitals[0];
        var reputation = hospital.Reputation;

        // Rating 10 would cost reputation outside free build mode; rating 1 would bring money.
        VipVisits.OnVipLeft(simulation, hospital, new VipBehaviour(1, 10));
        VipVisits.OnVipLeft(simulation, hospital, new VipBehaviour(1, 1));

        Assert.Equal(reputation + (2 * VipVisits.FreeBuildReputationReward), hospital.Reputation);
        Assert.Equal(0, hospital.Balance);
        Assert.Equal(0, hospital.Vips.PleasedThisYear);
    }

    [Fact]
    public void ThereIsNoWinningInFreeBuildMode()
    {
        var simulation = NewSimulation(Level.WithFreeBuild());
        while (simulation.Date.Month < 5)
        {
            simulation.Step();
        }

        Assert.Equal(EndOutcome.None, simulation.Hospitals[0].Outcome);
    }

    [Fact]
    public void SavesKeepFreeBuildMode()
    {
        var simulation = NewSimulation(Level.WithFreeBuild());
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream, Content);

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.True(restored.Rules.FreeBuild);
        Assert.Equal(0, restored.Rules.ObjectCost("cabinet"));
        Assert.Equal(0, restored.Hospitals[0].MaxLoan);
        restored.Hospitals[0].Receive(1, 500, TransactionKind.Cheat, string.Empty);
        Assert.Equal(0, restored.Hospitals[0].Balance);

        // A level played with money stays so.
        stream.SetLength(0);
        NewSimulation(Level).Save(stream);
        stream.Position = 0;
        Assert.False(Simulation.Load(stream, Content).Rules.FreeBuild);
    }

    private static void Run(Simulation simulation, Command command)
    {
        simulation.Schedule(0, command);
        simulation.Step();
    }

    /// <summary>A 12 × 10 hospital (parcel 1) with a parcel for sale (2) along the bottom row.</summary>
    private static Simulation NewSimulation(LevelRules rules)
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
        map.ParcelCount = 3;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Parcel = (ushort)(y == map.Height - 1 ? 2 : 1);
                map[x, y].Set(TileFlags.Buildable | TileFlags.Hospital | TileFlags.BuildableNorth | TileFlags.BuildableEast | TileFlags.BuildableSouth | TileFlags.BuildableWest, y != map.Height - 1);
            }
        }

        map.UpdatePathfinding();
        map.UpdateShadows();
        return new Simulation(map, seed: 7, Content, rules);
    }
}
