using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;

namespace CorsixNet.Sim.Tests;

/// <summary>The monthly figures kept for the graphs.</summary>
public class StatisticsTests
{
    [Fact]
    public void EachMonthEndRecordsTheMonthsMoneyAndTheFiguresSoFar()
    {
        var map = TestMaps.FromRows("....", "....");
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        map[0, 0].Parcel = 1;
        var simulation = new Simulation(map, seed: 4, rules: new LevelRules(10_000, [], [], startReputation: 600));
        var hospital = simulation.Hospitals[0];
        Assert.Equal([new MonthStatistics(0, 0, 0, 10_000, 0, 0, 0, 600)], hospital.Statistics);

        hospital.Spend(simulation.Tick, 300, TransactionKind.BuyObject, "desk");
        hospital.Receive(simulation.Tick, 50, TransactionKind.Treatment, "diag_gp");
        hospital.Cured = 2;
        while (simulation.Date.Month == 1)
        {
            simulation.Step();
        }

        Assert.Equal(2, hospital.Statistics.Count);
        Assert.Equal(new MonthStatistics(50, 300, 0, 9750, 0, 2, 0, 600), hospital.Statistics[1]);
        Assert.Equal((0L, 0L), (hospital.MoneyInThisMonth, hospital.MoneyOutThisMonth));

        // The heating bill is money out, paid at the month's end before the figures are taken.
        simulation.Schedule(0, new SetRadiatorHeatCommand(10));
        simulation.AddEntity(id => new RoomObject(id, "radiator", new(0, 0), Direction.North, 0));
        while (simulation.Date.Month == 2)
        {
            simulation.Step();
        }

        Assert.Equal(3, hospital.Statistics.Count);
        Assert.Equal(75, hospital.Statistics[2].MoneyOut);
        Assert.Equal(9750 - 75, hospital.Statistics[2].Balance);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(hospital.Statistics, restored.Hospitals[0].Statistics);
    }
}
