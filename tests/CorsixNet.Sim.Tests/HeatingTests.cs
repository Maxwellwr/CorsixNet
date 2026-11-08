using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;

namespace CorsixNet.Sim.Tests;

/// <summary>The radiators' heat, the heating bill and the boiler breaking down.</summary>
public class HeatingTests
{
    [Fact]
    public void TheBillIsTheHeatTimesTheRadiatorsRunUpDailyAndPaidMonthly()
    {
        var hospital = new Hospital(0, 1000);
        var random = new SimRandom(1);
        Assert.Equal(Hospital.DefaultRadiatorHeat, hospital.RadiatorHeat);

        // 0.5 × 10 × 4 radiators × 7.5 = 150 a month.
        for (var day = 0; day < 30; day++)
        {
            hospital.OnEndDayHeating(random, radiators: 4, handymen: 0, daysInMonth: 30);
        }

        hospital.PayHeating(10);
        Assert.Equal(new Transaction(10, TransactionKind.Heating, string.Empty, -150, 850), hospital.Transactions[0]);

        // Less than half a unit waits for the next month.
        hospital.SetRadiatorHeat(1);
        hospital.OnEndDayHeating(random, radiators: 1, handymen: 0, daysInMonth: 31);
        hospital.PayHeating(20);
        Assert.Single(hospital.Transactions);
        Assert.Equal(7.5m / 31, hospital.AccruedHeating);
    }

    [Fact]
    public void TheHeatIsSetInTenthsFromOneToTen()
    {
        var hospital = new Hospital(0, 0);

        Assert.True(hospital.SetRadiatorHeat(8));
        Assert.Equal(8, hospital.RadiatorHeat);
        hospital.SetRadiatorHeat(20);
        Assert.Equal(Hospital.MaxRadiatorHeat, hospital.RadiatorHeat);
        hospital.SetRadiatorHeat(0);
        Assert.Equal(Hospital.MinRadiatorHeat, hospital.RadiatorHeat);
    }

    [Fact]
    public void TheBoilerOfAnOpenHospitalWithTooFewHandymenBreaksAndIsRepaired()
    {
        var hospital = new Hospital(0, 0);
        var random = new SimRandom(3);
        hospital.SetRadiatorHeat(7);

        // Closed, or with a handyman for every 8 radiators, it never breaks.
        for (var day = 0; day < 2000; day++)
        {
            hospital.OnEndDayHeating(random, radiators: 9, handymen: 0, daysInMonth: 30);
            hospital.OnEndDayHeating(random, radiators: 8, handymen: 1, daysInMonth: 30);
            Assert.False(hospital.IsHeatingBroken);
        }

        hospital.Open();
        var days = 0;
        while (!hospital.IsHeatingBroken)
        {
            Assert.True(days++ < 2000, "The boiler should break within a few hundred days.");
            hospital.OnEndDayHeating(random, radiators: 9, handymen: 1, daysInMonth: 30);
        }

        Assert.Contains(hospital.RadiatorHeat, new[] { 0, Hospital.MaxRadiatorHeat });
        Assert.InRange(hospital.BoilerRepairCount, 10, 30);
        Assert.False(hospital.SetRadiatorHeat(3));

        // With 9 radiators and one handyman a repair a day; with two, 9 < 10 radiators, three a day.
        var left = hospital.BoilerRepairCount;
        hospital.OnEndDayHeating(random, radiators: 9, handymen: 1, daysInMonth: 30);
        Assert.Equal(left - 1, hospital.BoilerRepairCount);
        while (hospital.IsHeatingBroken)
        {
            left = hospital.BoilerRepairCount;
            hospital.OnEndDayHeating(random, radiators: 9, handymen: 2, daysInMonth: 30);
            Assert.True(!hospital.IsHeatingBroken || hospital.BoilerRepairCount == left - 3);
        }

        Assert.Equal(7, hospital.RadiatorHeat);
    }

    [Fact]
    public void TheMonthsBillGoesByTheRadiatorsInTheHospital()
    {
        var map = TestMaps.FromRows("....", "....");
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        map[0, 0].Parcel = 1;
        map[1, 0].Parcel = 1;
        var simulation = new Simulation(map, seed: 1, rules: new LevelRules(1000, [], []));
        simulation.AddEntity(id => new RoomObject(id, "radiator", new(0, 0), Direction.North, 0));
        simulation.AddEntity(id => new RoomObject(id, "radiator", new(1, 0), Direction.North, 0));
        simulation.AddEntity(id => new RoomObject(id, "radiator", new(2, 0), Direction.North, 0)); // outside
        simulation.Schedule(0, new SetRadiatorHeatCommand(10));
        while (simulation.Date.Month == 1)
        {
            simulation.Step();
        }

        // 1 × 10 × 2 radiators × 7.5 = 150 for January.
        var hospital = simulation.Hospitals[0];
        Assert.Equal(10, hospital.RadiatorHeat);
        Assert.Equal(new Transaction(simulation.Tick - 1, TransactionKind.Heating, string.Empty, -150, 850), hospital.Transactions[0]);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(hospital.DisasterlessDays, restored.Hospitals[0].DisasterlessDays);
    }
}
