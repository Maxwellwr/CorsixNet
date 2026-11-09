using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Sim.Tests;

/// <summary>The player's own salary and the offer on winning (CorsixTH's player_salary and salary_offer).</summary>
public class PlayerSalaryTests
{
    [Fact]
    public void TheSalaryRisesEachMonthByAtLeastASixthOfTheMaximum()
    {
        var simulation = NewSimulation(new LevelRules(10_000, [], [], scoreMaxIncrease: 300));
        var hospital = simulation.Hospitals[0];
        Assert.Equal(10_000, hospital.PlayerSalary);

        // Reputation 500 and no deaths: 30 + 0 is below the least, 50.
        StepToNextMonth(simulation);
        Assert.Equal(10_050, hospital.PlayerSalary);

        // Raised by hand: 30 + 200 / 1.
        hospital.RaiseSalary(300);
        Assert.Equal(10_100, hospital.PlayerSalary);
    }

    [Fact]
    public void WinningOffersTheSalaryWithABonusUnlessTheHospitalCheated()
    {
        var simulation = NewSimulation(LevelRules.Free);
        var hospital = simulation.Hospitals[0];
        hospital.End(EndOutcome.Won, null, simulation.Random);
        Assert.InRange(hospital.SalaryOffer, 10_400, 10_900);

        // A hospital that cheated before gets no bonus. (The win cheat itself marks the hospital only afterwards, as in
        // CorsixTH.)
        var cheater = NewSimulation(LevelRules.Free);
        cheater.Schedule(0, new CheatCommand(CheatKind.Money));
        cheater.Schedule(0, new CheatCommand(CheatKind.WinLevel));
        cheater.Step();
        Assert.Equal(EndOutcome.Won, cheater.Hospitals[0].Outcome);
        Assert.Equal(10_000, cheater.Hospitals[0].SalaryOffer);
    }

    [Fact]
    public void LevelThreeRaisesTheSalaryAtTheEndOfTheThirdYear()
    {
        var simulation = NewSimulation(new LevelRules(10_000, [], [], levelNumber: 3));
        var hospital = simulation.Hospitals[0];
        while (simulation.Date.Year < 3 || simulation.Date.Month < 12 || simulation.Date.Day < 31)
        {
            simulation.Step();
        }

        var before = hospital.PlayerSalary;
        StepToNextMonth(simulation);
        Assert.InRange(hospital.PlayerSalary - before, 8000 + 50, 20_000 + 300);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(3, restored.Rules.LevelNumber);
    }

    private static void StepToNextMonth(Simulation simulation)
    {
        var month = simulation.Date.Month;
        while (simulation.Date.Month == month)
        {
            simulation.Step();
        }
    }

    private static Simulation NewSimulation(LevelRules rules) => new(new LevelMap(6, 6, playerCount: 1), seed: 5, rules: rules);
}
