using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Sim.Tests;

/// <summary>What the difficulty changes (CorsixTH's Map:getDifficulty and its uses in hospital.lua).</summary>
public class DifficultyTests
{
    [Theory]
    [InlineData(Difficulty.Easy, 300)]
    [InlineData(Difficulty.Medium, 200)]
    [InlineData(Difficulty.Hard, 150)]
    public void TheBoilerHoldsOutLongerOnEasierLevels(Difficulty difficulty, int days)
    {
        var hospital = new Hospital(0, 0);
        var random = new SimRandom(5);
        for (var i = 0; i < 200; i++)
        {
            hospital.ScheduleNextDisaster(random, difficulty);
            Assert.InRange(hospital.DisasterlessDays, days - 10, days + 10);
        }
    }

    [Fact]
    public void TheDifficultyIsPartOfTheLevelAndSaved()
    {
        var map = new LevelMap(10, 10, playerCount: 1);
        var simulation = new Simulation(map, seed: 1, rules: new LevelRules(10_000, [], [], difficulty: Difficulty.Hard));
        Assert.InRange(simulation.Hospitals[0].DisasterlessDays, 140, 160);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(Difficulty.Hard, restored.Rules.Difficulty);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void AnUnknownDifficultyIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new LevelRules(0, [], [], difficulty: (Difficulty)4));
}
