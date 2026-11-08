using CorsixNet.Sim.Economy;

namespace CorsixNet.Sim.Tests;

/// <summary>Winning and losing a level, and the progress report.</summary>
public class EndConditionsTests
{
    [Fact]
    public void AHospitalWinsAtTheEndOfMarchAtTheEarliest()
    {
        var simulation = NewSimulation(5000, win: [new(EndCriterion.Balance, 1, AtLeast: true, 1000, 0, 5000)]);
        var hospital = simulation.Hospitals[0];

        while (simulation.Date.Month < 3)
        {
            simulation.Step();
            Assert.Equal(EndOutcome.None, hospital.Outcome);
        }

        while (simulation.Date.Month < 4)
        {
            simulation.Step();
        }

        Assert.Equal(EndOutcome.Won, hospital.Outcome);
        Assert.Null(hospital.LostTo);
    }

    [Fact]
    public void AHospitalLosesOnlyAtTheYearEndAfterItsAwards()
    {
        // Lose with less than 2,000 in the bank. The fines of the year end take 3,000 off the 4,000 it starts with.
        var lose = new EndGoal(EndCriterion.Balance, 1, AtLeast: false, 2000, 3000, 4000);
        var simulation = NewSimulation(4000, lose: [lose]);
        var hospital = simulation.Hospitals[0];

        while (simulation.Date.Year == 1)
        {
            Assert.Equal(EndOutcome.None, hospital.Outcome);
            simulation.Step();
        }

        Assert.Equal(1000, hospital.Balance);
        Assert.Equal(EndOutcome.Lost, hospital.Outcome);
        Assert.Equal(lose, hospital.LostTo);
    }

    [Fact]
    public void EveryGoalOfAGroupMustBeMet()
    {
        var simulation = NewSimulation(
            100,
            win: [new(EndCriterion.Balance, 1, true, 50, 0, 100), new(EndCriterion.NumCured, 1, true, 1, 0, 0)],
            lose:
            [
                new(EndCriterion.Balance, 1, false, 200, 0, 100), new(EndCriterion.Reputation, 1, false, 100, 0, 500),
                new(EndCriterion.Reputation, 2, false, 600, 0, 500), new(EndCriterion.Balance, 2, false, 300, 0, 100),
            ]);
        var hospital = simulation.Hospitals[0];
        var conditions = simulation.Rules.EndConditions;

        // Group 1 of the lose goals is half met; all of group 2 is, and it loses to its last goal by criterion.
        var (outcome, lostTo) = conditions.Check(simulation, hospital);
        Assert.Equal(EndOutcome.Lost, outcome);
        Assert.Equal(EndCriterion.Balance, lostTo!.Value.Criterion);
        Assert.Equal(2, lostTo.Value.Group);

        // A win comes first.
        hospital.Cured = 1;
        Assert.Equal((EndOutcome.Won, (EndGoal?)null), conditions.Check(simulation, hospital));
    }

    [Fact]
    public void ThePercentagesFollowCorsixTh()
    {
        var hospital = new Hospital(0, 0);
        Assert.Equal((0, (int?)0), (hospital.PercentageCured, hospital.PercentageKilled));

        // Only patients who went home uncured: CorsixTH's 0 / 0, which meets no goal.
        hospital.NotCured = 1;
        Assert.Equal((0, (int?)null), (hospital.PercentageCured, hospital.PercentageKilled));

        hospital.Cured = 4;
        hospital.Deaths = 3;
        Assert.Equal(50, hospital.PercentageCured); // 4 / 8
        Assert.Equal(43, hospital.PercentageKilled); // 3 / 7 = 42.86

        hospital.Cured = 5;
        hospital.Deaths = 1;
        Assert.Equal(71, hospital.PercentageCured); // 5 / 7 = 71.43
        Assert.Equal(17, hospital.PercentageKilled); // 1 / 6 = 16.67, and 0.5 rounds up

        var simulation = NewSimulation(0, lose: [new(EndCriterion.PercentageKilled, 1, true, 10, 0, 0)]);
        Assert.Equal(EndOutcome.None, simulation.Rules.EndConditions.Check(simulation, new Hospital(0, 0) { NotCured = 2 }).Outcome);
    }

    [Fact]
    public void TheOtherMeasures()
    {
        var simulation = NewSimulation(0);
        var hospital = simulation.Hospitals[0];

        // Without staff or patients the averages fall back on CorsixTH's 0.5.
        Assert.Equal(50m, EndConditions.Measure(simulation, hospital, EndCriterion.StaffHappiness));
        Assert.Equal(50m, EndConditions.Measure(simulation, hospital, EndCriterion.PatientHappiness));
        Assert.Equal(1m, EndConditions.Measure(simulation, hospital, EndCriterion.MonthsPlayed));
        Assert.Equal(20_000m, EndConditions.Measure(simulation, hospital, EndCriterion.Value));
        Assert.Equal(500m, EndConditions.Measure(simulation, hospital, EndCriterion.Reputation));
    }

    [Fact]
    public void GoalsAreOnePerCriterionAndGroup()
    {
        var conditions = new EndConditions(
            [new(EndCriterion.Balance, 2, true, 10, 0, 0), new(EndCriterion.Balance, 2, true, 20, 0, 0), new((EndCriterion)0, 1, true, 0, 0, 0), new(EndCriterion.Reputation, 1, true, 700, 0, 500)],
            []);

        Assert.Equal([new(EndCriterion.Reputation, 1, true, 700, 0, 500), new(EndCriterion.Balance, 2, true, 20, 0, 0)], conditions.WinGoals);
    }

    [Fact]
    public void TheProgressReportShowsTheLoseGoalsNearAndTheBestWinGroup()
    {
        var simulation = NewSimulation(
            1000,
            win:
            [
                new(EndCriterion.Reputation, 1, true, 900, 0, 500), new(EndCriterion.NumCured, 1, true, 100, 0, 0),
                new(EndCriterion.Balance, 2, true, 500, 0, 1000), new(EndCriterion.Value, 2, true, 1_000_000, 0, 0),
                new(EndCriterion.Reputation, 3, true, 999, 0, 500),
            ],
            lose: [new(EndCriterion.Balance, 1, false, 0, 2000, 1000), new(EndCriterion.PercentageKilled, 1, true, 50, 30, 0)]);
        var hospital = simulation.Hospitals[0];

        var report = simulation.Rules.EndConditions.ProgressReport(simulation, hospital);

        // The balance is past its bound of 2,000, half way to 0; the killed percentage of 0 is not near 30. Group 2 is
        // the best win group (half met), and its balance goal gives way to the lose goal for the balance.
        Assert.Equal(
            [
                new ProgressEntry(new(EndCriterion.Balance, 1, false, 0, 2000, 1000), true, 0.5m),
                new ProgressEntry(new(EndCriterion.Value, 2, true, 1_000_000, 0, 0), false, null),
            ],
            report);
    }

    [Fact]
    public void SavesKeepTheGoalsAndTheOutcome()
    {
        var simulation = NewSimulation(5000, win: [new(EndCriterion.Balance, 1, true, 1000, 0, 5000)], lose: [new(EndCriterion.Reputation, 1, false, 100, 200, 500)]);
        while (simulation.Date.Month < 4)
        {
            simulation.Step();
        }

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);

        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(EndOutcome.Won, restored.Hospitals[0].Outcome);
        Assert.Equal(simulation.Rules.EndConditions.LoseGoals, restored.Rules.EndConditions.LoseGoals);
    }

    private static Simulation NewSimulation(int startCash, EndGoal[]? win = null, EndGoal[]? lose = null)
    {
        var map = TestMaps.FromRows("....", "....");
        var rules = new LevelRules(startCash, [], [], endConditions: new EndConditions(win ?? [], lose ?? []));
        return new Simulation(map, seed: 5, rules: rules);
    }
}
