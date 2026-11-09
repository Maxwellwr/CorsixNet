using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;

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

    private static void Run(Simulation simulation, CheatKind cheat)
    {
        simulation.Schedule(0, new CheatCommand(cheat));
        simulation.Step();
    }

    private static Simulation NewSimulation() => new(TestMaps.FromRows("....", "...."), seed: 6, rules: new LevelRules(1000, [], []));
}
