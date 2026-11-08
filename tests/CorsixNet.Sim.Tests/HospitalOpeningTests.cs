using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;

namespace CorsixNet.Sim.Tests;

/// <summary>The opening watch: the hospital opens a hundred days in, or when the player opens it.</summary>
public class HospitalOpeningTests
{
    [Fact]
    public void TheHospitalOpensWhenTheWatchRunsDown()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        Assert.False(hospital.IsOpen);
        Assert.Equal(Hospital.OpeningSegments, hospital.OpenTimer);

        // 13 segments of 385 hours, and one more hour for the watch to notice it has run down.
        Assert.Equal((13 * (Hospital.OpeningTickRate + 1)) + 1, hospital.HoursUntilOpen);
        Assert.Equal(5006, hospital.HoursUntilOpen);

        for (var hour = 5006; hour > 1; hour--)
        {
            Assert.Equal(hour, hospital.HoursUntilOpen);
            simulation.Step();
            Assert.False(hospital.IsOpen);
        }

        Assert.Equal(-1, hospital.OpenTimer);
        simulation.Step();
        Assert.True(hospital.IsOpen);
        Assert.Equal(0, hospital.HoursUntilOpen);
    }

    [Fact]
    public void ThePlayerMayOpenEarly()
    {
        var simulation = NewSimulation();

        simulation.Schedule(0, new OpenHospitalCommand());
        simulation.Step();

        Assert.True(simulation.Hospitals[0].IsOpen);
    }

    [Fact]
    public void SaveAndLoadKeepTheWatch()
    {
        var original = NewSimulation();
        for (var i = 0; i < 1000; i++)
        {
            original.Step();
        }

        using var stream = new MemoryStream();
        original.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);

        Assert.Equal(original.Hospitals[0].HoursUntilOpen, restored.Hospitals[0].HoursUntilOpen);
        Assert.Equal(original.ComputeStateHash(), restored.ComputeStateHash());
    }

    private static Simulation NewSimulation()
    {
        var map = TestMaps.FromRows(
            "....",
            "....",
            "....");
        return new Simulation(map, seed: 1);
    }
}
