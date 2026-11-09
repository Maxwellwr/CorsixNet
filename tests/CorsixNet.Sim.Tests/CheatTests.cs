using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Disasters;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Sim.Tests;

/// <summary>CorsixTH's menu cheats.</summary>
public class CheatTests
{
    [Fact]
    public void MoneyComesFromNowhereAndMarksTheHospital()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];

        Run(simulation, CheatKind.Money);

        Assert.Equal(11_000, hospital.Balance);
        Assert.Equal(new Transaction(0, TransactionKind.Cheat, string.Empty, 10_000, 11_000), hospital.Transactions[0]);
        Assert.True(hospital.Cheated);
    }

    [Fact]
    public void LosingTheLevelIsNotCheating()
    {
        var simulation = NewSimulation();
        Run(simulation, CheatKind.LoseLevel);
        Assert.Equal(EndOutcome.Lost, simulation.Hospitals[0].Outcome);
        Assert.False(simulation.Hospitals[0].Cheated);

        var other = NewSimulation();
        Run(other, CheatKind.WinLevel);
        Assert.Equal(EndOutcome.Won, other.Hospitals[0].Outcome);
        Assert.True(other.Hospitals[0].Cheated);
    }

    [Fact]
    public void PricesMoveByFiftyPointsWithinTheLimits()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        var gp = hospital.CaseOf(DiseaseCatalog.Find(DiseaseCatalog.GpDiagnosis)!, simulation.Rules.Diseases);
        var bloaty = hospital.CaseOf(DiseaseCatalog.Find("bloaty_head")!, simulation.Rules.Diseases);
        hospital.SetTreatmentPrice(DiseaseCatalog.Find(DiseaseCatalog.GpDiagnosis)!, simulation.Rules.Diseases, 180);

        Run(simulation, CheatKind.IncreasePrices);
        Assert.Equal((200, 150), (gp.PricePercent, bloaty.PricePercent));

        Run(simulation, CheatKind.DecreasePrices);
        Run(simulation, CheatKind.DecreasePrices);
        Run(simulation, CheatKind.DecreasePrices);
        Assert.Equal((50, 50), (gp.PricePercent, bloaty.PricePercent));
    }

    [Fact]
    public void DeathsCanBeForgottenAndTheReputationMaxedOut()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        hospital.Deaths = 4;
        hospital.DeathsThisYear = 3;

        Run(simulation, CheatKind.ResetDeathCount);
        Run(simulation, CheatKind.MaxReputation);

        Assert.Equal((0, 0), (hospital.Deaths, hospital.DeathsThisYear));
        Assert.Equal(Hospital.MaxReputation, hospital.Reputation);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.True(restored.Hospitals[0].Cheated);
    }

    [Fact]
    public void TheMonthAndTheYearCanBeEndedAtOnce()
    {
        var simulation = NewSimulation();
        Run(simulation, CheatKind.EndMonth);

        // The jump lands on the month's last hour, which the tick then ends.
        Assert.Equal(1, simulation.Tick);
        Assert.Equal(new GameDate(1, 2, 1, 0), simulation.Date);
        Assert.Equal((31 * GameDate.HoursPerDay) - 1, simulation.SkippedHours);
        Assert.True(simulation.Hospitals[0].Cheated);

        simulation.Schedule(0, new CheatCommand(CheatKind.EndYear));
        simulation.Step();
        Assert.Equal(new GameDate(2, 1, 1, 0), simulation.Date);

        // Money moves at the calendar's hour, and the skipped hours are saved.
        simulation.Schedule(0, new CheatCommand(CheatKind.Money));
        simulation.Step();
        Assert.Equal(new GameDate(2, 1, 1, 0).ToHours(), simulation.Hospitals[0].Transactions[0].Hour);
        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.Date, restored.Date);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void AnEmergencyPlannedInTheSkippedDaysIsReplanned()
    {
        var map = new LevelMap(10, 10, playerCount: 1);
        var simulation = new Simulation(map, seed: 1, rules: new LevelRules(10_000, [], [], emergencies:
        [
            new PlannedEmergency(0, 1, 1, 4, 6, 16, 75, 400),
            new PlannedEmergency(1, 3, 3, 4, 6, 16, 75, 400),
        ]));
        var state = simulation.Hospitals[0].Emergencies;
        Assert.Equal(0, state.NextIndex);
        state.NextDay = 15;

        Run(simulation, CheatKind.EndMonth);

        Assert.Equal(1, state.NextIndex);
        Assert.Equal(3, state.NextMonth);
    }

    private static void Run(Simulation simulation, CheatKind cheat)
    {
        simulation.Schedule(0, new CheatCommand(cheat));
        simulation.Step();
    }

    private static Simulation NewSimulation() => new(TestMaps.FromRows("....", "...."), seed: 6, rules: new LevelRules(1000, [], []));
}
